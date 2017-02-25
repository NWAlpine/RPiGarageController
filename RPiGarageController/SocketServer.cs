using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Windows.Networking.Sockets;
using Windows.Storage.Streams;      // DataWriter

namespace RPiGarageController
{
    internal class SocketServer
    {
        private readonly int _port;
        public int Port
        {
            get { return _port; }
        }
        private StreamSocketListener listener;
        private DataWriter _writer;

        public delegate void DataReceived(string data);
        public event DataReceived OnDataReceived;

        public delegate void Error(string message);
        public event Error OnError;

        // ctor
        public SocketServer(int port)
        {
            _port = port;
        }

        public async void Begin()
        {
            try
            {
                if (listener != null)
                {
                    await listener.CancelIOAsync();
                    listener.Dispose();
                    listener = null;
                }

                listener = new StreamSocketListener();

                listener.ConnectionReceived += Listener_ConnectionReceived;
                await listener.BindServiceNameAsync(Port.ToString());

            }
            catch (Exception e)
            {
                OnError(e.Message);
            }
        }

        private async void Listener_ConnectionReceived(StreamSocketListener sender, StreamSocketListenerConnectionReceivedEventArgs args)
        //private void Listener_ConnectionReceived(StreamSocketListener sender, StreamSocketListenerConnectionReceivedEventArgs args)
        {
            // these need to be wrapped in a Using statement
            var reader = new DataReader(args.Socket.InputStream);
            _writer = new DataWriter(args.Socket.OutputStream);
            try
            {
                while (true)
                {
                    uint sizeFieldCount = await reader.LoadAsync(sizeof(uint));

                    // if a disconnection happens
                    if (sizeFieldCount != sizeof(uint))
                        return;

                    uint stringLength = reader.ReadUInt32();

                    // read data from the InputStream
                    uint actualStringLength = await reader.LoadAsync(stringLength);

                    if (stringLength != actualStringLength)
                        return;

                    // if a disconnection happens this will be not equal
                    if (OnDataReceived != null)
                    {
                        string data = reader.ReadString(actualStringLength);
                        OnDataReceived(data);
                    }
                }
            }
            catch (Exception ex)
            {
                if (OnError != null)
                    OnError(ex.Message);
            }
        }

        public async void Send(string message)
        {
            if (_writer != null)
            {
                _writer.WriteUInt32(_writer.MeasureString(message));    // send the size of the message
                _writer.WriteString(message);                           // send the message
            }

            try
            {
                await _writer.StoreAsync();
                await _writer.FlushAsync();
            }
            catch (Exception ex)
            {
                if (OnError != null)
                    OnError(ex.Message);
            }
        }
    }
}
