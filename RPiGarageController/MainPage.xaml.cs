#region using directives

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

//using System.Net;
//using System.Net.Sockets;

using Windows.Devices.Enumeration;
using Windows.Devices.SerialCommunication;
//using Windows.Networking.Sockets;
using Windows.Storage.Streams;      // DataWriter
using Windows.System.Threading;

#endregion

/* The Blank Page item template is documented at http://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409
 * MSDN serial communication https://msdn.microsoft.com/en-us/library/windows/apps/windows.devices.serialcommunication.aspx
 * 
 * This needs to be added in the Package.appmanifest file inside the <Capabilities> field
    <DeviceCapability Name="serialcommunication">
      <Device Id="any">
        <Function Type="name:serialPort" />
      </Device>
    </DeviceCapability>

Execute on RPi2:
----------------
Change platform from “x86” to “ARM”
Change target device to “Remote Machine”
Enter remote machine name (in my case that would be “minwinpc”)
Set authentication mode to “Universal (Unencrypted protocol)”.
In the project properties\debug is where the auth and target name are stored.
 */

namespace RPiGarageController
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        private SocketServer socket;

        DataReader dataReaderObject = null;
        SerialDevice device = null;

        UInt32 bytesRead;

        // degree symbol = alt+0176 
        const string celciusSymbol = "°C";
        const string fahrenheitSymbol = "°F";
        bool isCelcius = true;  // default

        CancellationTokenSource cancellationToken = new CancellationTokenSource();

        int i = 0;
        public MainPage()
        {
            // set initial values to zero so they get sent
            ResetDataValues();
            /*
            DataProperties.GarageDoorState = -1;
            DataProperties.KitchenDoorOpen = -1;
            DataProperties.GarageLight = -1;
            DataProperties.GarageBayAOccupiedState = -1;
            DataProperties.GarageBayBOccupiedState = -1;
            */

            this.InitializeComponent();
            ConnectSerialDevice();
        }

        private async void ConnectSerialDevice()
        {
            // this call returns ALL devices attached to the computer. Could be over 400...
            // DeviceInformationCollection col = await DeviceInformation.FindAllAsync();

            // this selectes a specific serial port, if already known
            // string _serialSelector = SerialDevice.GetDeviceSelector("COM3");

            // Find all the serial devices
            // first get a AQS query that DeviceInfo class will use to find all serial devices
            // DeviceWatcher class can also be used, see sample
            string _serialSelector = SerialDevice.GetDeviceSelector();
            DeviceInformationCollection devices = await DeviceInformation.FindAllAsync(_serialSelector);

            // debug container for looking at all the serial devices...
            List<string> serialDevices = new List<string>();

            if (devices.Any())
            {
                foreach (var item in devices)
                {
                    serialDevices.Add(item.Name);
                }

                // TODO: enum and pick from UI, for now there will only be one.
                var deviceId = devices.First().Id;
                device = await SerialDevice.FromIdAsync(deviceId);

                if (device != null)
                {
                    device.BaudRate = 9600;
                    device.StopBits = SerialStopBitCount.One;
                    device.DataBits = 8;
                    device.Parity = SerialParity.None;
                    device.Handshake = SerialHandshake.None;

                    device.ReadTimeout = TimeSpan.FromMilliseconds(500);
                    device.WriteTimeout = TimeSpan.FromMilliseconds(500);
                }
                else
                {
                    // TODO: error handling?
                }

                // Begin the task to read the port
                Listen();

                txtStatus.Text = "Running.";

                socket = new SocketServer(9000);
                ThreadPool.RunAsync(x => {
                    socket.OnError += socket_OnError;
                    socket.OnDataReceived += Socket_OnDataReceived;
                    socket.Begin();
                });
            }
        }

        /// <summary>
        /// process any data received
        /// </summary>
        /// <param name="data">data recieved </param>
        private void Socket_OnDataReceived(string data)
        {
            // replace with data read from Arduino
            //socket.Send("Data Sent: " + data);

            // parse out the prefix action
            string prefix = data.Substring(0, 1);

            switch (prefix)
            {
                case "r":
                    ResetDataValues();
                    break;

                default:
                    break;
            }
        }

        private void socket_OnError(string message)
        { }



        private async void Listen()
        {
            try
            {
                if (device != null)
                {
                    dataReaderObject = new DataReader(device.InputStream);

                    while (true)
                    {
                        await ReadAsync(cancellationToken.Token);
                    }
                }
            }
            catch (Exception)
            {
                // crap exception!
                throw;
            }
        }

        private async Task ReadAsync(CancellationToken cancellationToken)
        {
            Task<UInt32> loadAsyncTask;
            uint readBufferLength = 1024;

            cancellationToken.ThrowIfCancellationRequested();

            dataReaderObject.InputStreamOptions = InputStreamOptions.Partial;

            loadAsyncTask = dataReaderObject.LoadAsync(readBufferLength).AsTask(cancellationToken);

            bytesRead = await loadAsyncTask;
            if (bytesRead > 0)
            {
                string result = dataReaderObject.ReadString(bytesRead);

                //lstBox.Items.Add(result);

                ProcessData(result);
            }
        }

        private void ProcessData(string data)
        {
            /*
            example data:
            gu1 - Garage Upper
            gl0 - Garage Lower
            kd1 - Kitchen Door
            la145 - Light Reading
            sa117 - Sonar Reading A
            sb117 - Sonar Reading B
            tc16.90 - Temp C
            th59.70 - Humidity
            ta16.20 - Heat Index C
            ti16.95 - Internal Temp C (from breakout board)

            dataReaderObject.ReadString(bytesRead)
            "gu1\r\ngl0\r\nde1\r\nla394\r\nsa111\r\ntc18.10\r\nth53.00\r\nta17.35\r\nti17.81\r\n"
            */

            string sensorId;

            char[] delimiters = {'\r', '\n' };
            string[] resuts = data.Split(delimiters);

            foreach (string sensorGroup in resuts)
            {
                if (!string.IsNullOrEmpty(sensorGroup))
                {
                    // parse which sensor
                    // TODO: change to use the properties for the sensor prop string rather than the hard coded chars
                    sensorId = sensorGroup.Substring(0, 1);

                    switch (sensorId)
                    {
                        case "g":
                            // garage door
                            AnalyzeGarageDoorData(sensorGroup);
                            break;

                        case "k":
                            // kitchen door data
                            AnalyzeKitchenDoorData(sensorGroup);
                            break;

                        case "l":
                            // garage lights
                            AnalyzeLightData(sensorGroup);
                            break;

                        case "s":
                            // car port - sonar data
                            AnalyzeSonarData(sensorGroup);
                            break;

                        case "t":
                            // temp data
                            AnalyzeTempData(sensorGroup);
                            break;

                        default:
                            break;
                    }
                }
            }
        }

        internal void AnalyzeGarageDoorData(string data)
        {
            // door sensor logic
            // lower    upper
            //  0       1       = Door Closed
            //  1       0       = Door Open
            //  0       0       = Door in transition state

            // gl = lower switch
            // gu = upper switch

            string doorSwitch = data.Substring(1, 1);   // which switch
            string reading = data.Substring(2, 1);      // get data
            int value;
            if (!Int32.TryParse(reading, out value))
            {
                // TODO: error handling
            }

            switch (doorSwitch)
            {
                case "l":
                    // TODO: make enums for SwitchState: 0 = switch closed, 1 = switch open
                    // GarageDoorState: Closed = 0, Open = 1, Closing = 2, , Opening = 3;
                    // Send data only if the state has changed.
                    if (value == 0)
                    {
                        // Garage Door is Closed, check if still closed, if so no changes
                        if (DataProperties.GarageDoorState != (int)DataProperties.DoorState.Closed)
                        {
                            txtGarDoorValue.Text = "Closed.";
                            DataProperties.LastGarageDoorSwitchState = 0;
                            DataProperties.GarageDoorState = (int)DataProperties.DoorState.Closed;
                            socket.Send(string.Format("{0}{1}", DataProperties.GarageDoorPrefix, DataProperties.GarageDoorState));
                        }
                    }
                    else
                    {
                        // Both switches are open, determine if door is opening or closing
                        if (DataProperties.GarageUpperSwitchState == 1 && DataProperties.LastGarageDoorSwitchState == 0)
                        {
                            if (DataProperties.GarageDoorState != (int)DataProperties.DoorState.Opening)
                            {
                                txtGarDoorValue.Text = "Opening...";
                                DataProperties.GarageDoorState = (int)DataProperties.DoorState.Opening;
                                socket.Send(string.Format("{0}{1}", DataProperties.GarageDoorPrefix, DataProperties.GarageDoorState));
                                break;
                            }
                        }
                        else if (DataProperties.GarageUpperSwitchState == 1 && DataProperties.LastGarageDoorSwitchState == 1)
                        {
                            if (DataProperties.GarageDoorState != (int)DataProperties.DoorState.Closing)
                            {
                                txtGarDoorValue.Text = "Closing...";
                                DataProperties.GarageDoorState = (int)DataProperties.DoorState.Closing;
                                socket.Send(string.Format("{0}{1}", DataProperties.GarageDoorPrefix, DataProperties.GarageDoorState));
                                break;
                            }
                        }

                        if (DataProperties.GarageUpperSwitchState == 0)
                        {
                            if (DataProperties.GarageDoorState != (int)DataProperties.DoorState.Open)
                            {
                                txtGarDoorValue.Text = "Open.";
                                DataProperties.LastGarageDoorSwitchState = 1;
                                DataProperties.GarageDoorState = (int)DataProperties.DoorState.Open;
                                socket.Send(string.Format("{0}{1}", DataProperties.GarageDoorPrefix, DataProperties.GarageDoorState));
                            }
                        }
                    }
                    break;

                case "u":
                    DataProperties.GarageUpperSwitchState = value;
                    break;

                default:
                    break;
            }
        }

        internal void AnalyzeKitchenDoorData(string data)
        {
            // only send if state has changed
            // parse out the data i.e. de1 or de0
            string reading = data.Substring(2, 1);
            int value;
            if (Int32.TryParse(reading, out value))
            {
                if (value == 1)
                {
                    if (DataProperties.KitchenDoorOpen != (int)DataProperties.DoorState.Open)
                    {
                        txtKitDoorValue.Text = "Open.";
                        DataProperties.KitchenDoorOpen = (int)DataProperties.DoorState.Open;
                        socket.Send(string.Format("{0}{1}", DataProperties.KitchenDoorPrefix, DataProperties.KitchenDoorOpen));
                    }
                }
                else
                {
                    if (DataProperties.KitchenDoorOpen != (int)DataProperties.DoorState.Closed)
                    {
                        txtKitDoorValue.Text = "Closed.";
                        DataProperties.KitchenDoorOpen = (int)DataProperties.DoorState.Closed;
                        socket.Send(string.Format("{0}{1}", DataProperties.KitchenDoorPrefix, DataProperties.KitchenDoorOpen));
                    }
                }
            }
            else
            {
                // TODO: better error handling or retry logic
                txtKitDoorValue.Text = "Error!";
            }
        }

        internal void AnalyzeLightData(string data)
        {
            // logic to analyze change in light: more light = smaller number, threshold is 80 if the number is less than 80, light is on
            // parse out the data, send data only if there is a change
            string reading = data.Substring(2);
            int value;
            if (Int32.TryParse(reading, out value))
            {
                if (value > DataProperties.LightOnThreshold)
                {
                    if (DataProperties.GarageLight != (int)DataProperties.LightState.Off)
                    {
                        txtGarLightsValue.Text = "Off.";
                        DataProperties.GarageLight = (int)DataProperties.LightState.Off;
                        socket.Send(string.Format("{0}{1}", DataProperties.LightPrefix, DataProperties.GarageLight));
                    }
                }
                else if (value < DataProperties.LightOnThreshold)
                {
                    if (DataProperties.GarageLight != (int)DataProperties.LightState.On)
                    {
                        txtGarLightsValue.Text = "On.";
                        DataProperties.GarageLight = (int)DataProperties.LightState.On;
                        socket.Send(string.Format("{0}{1}", DataProperties.LightPrefix, DataProperties.GarageLight));
                    }
                }
            }
            else
            {
                // TODO: better error handling
                txtGarLightsValue.Text = "Error!";
            }
        }

        internal void AnalyzeSonarData(string data)
        {
            // first get the bay, then the value
            // bigger the num, greater the distance.
            string bay = data.Substring(1, 1).ToLower();
            string reading = data.Substring(2);
            int value;
            int distance;
            if (Int32.TryParse(reading, out value))
            {
                switch (bay)
                {
                    // TODO: GarageBayAOccupied needs to change to something like GarageBayAOccupiedState
                    case "a":
                        distance = value / 2;   // convert to inches
                        if (distance < DataProperties.GarageBayOccupiedThreshold)
                        {
                            // bay A is occupied
                            if (DataProperties.GarageBayAOccupiedState != (int)DataProperties.GarageBay.Occupied)
                            {
                                txtGarBayAValue.Text = "Occupied.";
                                DataProperties.GarageBayAOccupiedState = (int)DataProperties.GarageBay.Occupied;
                                socket.Send(string.Format("{0}{1}", DataProperties.BayAPrefix, DataProperties.GarageBayAOccupiedState));
                            }
                        }
                        else
                        {
                            if (DataProperties.GarageBayAOccupiedState != (int)DataProperties.GarageBay.Vacant)
                            {
                                txtGarBayAValue.Text = "Vacant.";
                                DataProperties.GarageBayAOccupiedState = (int)DataProperties.GarageBay.Vacant;
                                socket.Send(string.Format("{0}{1}", DataProperties.BayAPrefix, DataProperties.GarageBayAOccupiedState));
                            }
                        }
                        break;

                    case "b":
                        distance = value / 2;   // convert to inches
                        if (distance < DataProperties.GarageBayOccupiedThreshold)
                        {
                            if (DataProperties.GarageBayBOccupiedState != (int)DataProperties.GarageBay.Occupied)
                            {
                                txtGarBayBValue.Text = "Occupied.";
                                DataProperties.GarageBayBOccupiedState = (int)DataProperties.GarageBay.Occupied;
                                socket.Send(string.Format("{0}{1}", DataProperties.BayBPrefix, DataProperties.GarageBayBOccupiedState));
                            }
                        }
                        else
                        {
                            if (DataProperties.GarageBayBOccupiedState != (int)DataProperties.GarageBay.Vacant)
                            {
                                txtGarBayBValue.Text = "Vacant.";
                                DataProperties.GarageBayBOccupiedState = (int)DataProperties.GarageBay.Vacant;
                                socket.Send(string.Format("{0}{1}", DataProperties.BayBPrefix, DataProperties.GarageBayBOccupiedState));
                            }
                        }
                        break;

                    default:
                        break;
                }
            }
            else
            {
                // TODO: better error handling
            }
        }

        internal void AnalyzeTempData(string data)
        {
            string tempId = data.Substring(1, 1);
            float result;

            switch (tempId)
            {
                // garage temp c
                // TODO: preserve the float!!
                case "i":
                    if (float.TryParse(data.Substring(2), out result))
                    {
                        // we have a value, compare to previous value to determine if a change is made.
                        if (DataProperties.LastGarageTemp != (int)result)
                        {
                            txtGarTempValue.Text = string.Format("{0} {1}", result.ToString(), isCelcius ? celciusSymbol : fahrenheitSymbol);
                            DataProperties.LastGarageTemp = (int)result;
                            socket.Send(string.Format("{0}{1}", DataProperties.InternalTempPrefix, DataProperties.LastGarageTemp));
                        }
                    }
                    else
                    {
                        // TODO: better error handling
                        txtGarTempValue.Text = "Error!";
                    }
                    break;
                
                // external temp
                case "c":
                    if (float.TryParse(data.Substring(2), out result))
                    {
                        if (DataProperties.LastFrontTemp != (int)result)
                        {
                            txtFrontTempValue.Text = string.Format("{0} {1}", result.ToString(), isCelcius ? celciusSymbol : fahrenheitSymbol);
                            DataProperties.LastFrontTemp = (int)result;
                            socket.Send(string.Format("{0}{1}", DataProperties.ExternalTempPrefix, DataProperties.LastFrontTemp));
                        }
                    }
                    else
                    {
                        // TODO: error handling
                        txtFrontTempValue.Text = "Error!";
                    }
                    break;

                // exteranl humidity
                case "h":
                    if (float.TryParse(data.Substring(2), out result))
                    {
                        if (DataProperties.LastFrontHumid != (int)result)
                        {
                            txtFronthumidValue.Text = string.Format("{0}{1}", result.ToString(), "%");
                            DataProperties.LastFrontHumid = (int)result;
                            socket.Send(string.Format("{0}{1}", DataProperties.ExternalHumidPrefix, DataProperties.LastFrontHumid));
                        }
                    }
                    else
                    {
                        // TODO: error handling
                        txtFronthumidValue.Text = "Error!";
                    }
                    break;

                // external heat index - NOTE: external HI prefix sent from garage node is "ta"
                // but "a" is the prefix used in the sonar bay A, sending "x" to the home controller for external heat index
                case "a":
                    if (float.TryParse(data.Substring(2), out result))
                    {
                        if (DataProperties.LastFrontHI != (int)result)
                        {
                            txtFrontHIValue.Text = string.Format("{0} {1}", result.ToString(), isCelcius ? celciusSymbol : fahrenheitSymbol);
                            DataProperties.LastFrontHI = (int)result;
                            socket.Send(string.Format("{0}{1}", DataProperties.ExternalHIPrefix, DataProperties.LastFrontHI));
                        }
                    }
                    else
                    {
                        // TODO: error handling
                        txtFrontHIValue.Text = "Error!";
                    }
                    break;

                default:
                    break;
            }
        }

        internal void ResetDataValues()
        {
            // reset values, temp values can be -1
            DataProperties.GarageDoorState = -1;
            DataProperties.KitchenDoorOpen = -1;
            DataProperties.GarageLight = -1;
            DataProperties.GarageBayAOccupiedState = -1;
            DataProperties.GarageBayBOccupiedState = -1;
            DataProperties.LastGarageTemp = -100;
            DataProperties.LastFrontTemp = -100;
            DataProperties.LastFrontHumid = -1;
            DataProperties.LastFrontHI = -100;
        }

        #region UI Handlers
        private void btnHello_Click(object sender, RoutedEventArgs e)
        {
            txtHello.Text = "Hello!";
            i++;
        }

        private void txtHello_TextChanged(object sender, TextChangedEventArgs e)
        {

        }

        #endregion
    }

    public class DataProperties
    {
        public static int LightOnThreshold
        {
            get { return 80; }
        }

        public static int GarageLight
        {
            get; set;
        }

        public static int GarageBayOccupiedThreshold
        {
            get { return 36; }
        }

        public static int GarageBayAOccupiedState
        {
            get; set;
        }

        public static int GarageBayBOccupiedState
        {
            get; set;
        }

        public static int KitchenDoorOpen
        {
            get; set;
        }

        public static int GarageUpperSwitchState
        {
            get; set;
        }

        public static int LastGarageDoorSwitchState
        {
            get; set;
        }

        public static int GarageDoorState
        {
            get; set;
        }

        public static int LastGarageTemp
        {
            get; set;
        }

        #region Front Temp Readings

        public static int LastFrontTemp
        { get; set; }

        public static int LastFrontHumid
        { get; set; }

        public static int LastFrontHI
        { get; set; }

        #endregion

        public enum DoorState
        {
            Closed,
            Open,
            Closing,
            Opening
        };

        public enum LightState
        {
            Off,
            On
        };

        public enum GarageBay
        {
            Occupied,
            Vacant
        };

        #region sensor prefix schema

        public static string GarageDoorPrefix
        {
            get { return "g"; }
        }

        public static string KitchenDoorPrefix
        {
            get { return "k"; }
        }

        public static string LightPrefix
        {
            get { return "l"; }
        }

        public static string BayAPrefix
        {
            get { return "a"; }
        }

        public static string BayBPrefix
        {
            get { return "b"; }
        }

        public static string InternalTempPrefix
        {
            get { return "i"; }
        }

        public static string ExternalTempPrefix
        {
            get { return "c"; }
        }

        public static string ExternalHumidPrefix
        {
            get { return "h"; }
        }

        public static string ExternalHIPrefix
        {
            get { return "x"; }
        }

        #endregion
    }
}
