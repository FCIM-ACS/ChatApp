using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace Client
{
    public partial class Client : Form
    {
        private bool connected = false;
        private Thread client = null;
        private struct MyClient
        {
            public string username;
            public string key;
            public TcpClient client;
            public NetworkStream stream;
            public byte[] buffer;
            public StringBuilder data;
            public EventWaitHandle handle;
        };
        private MyClient obj;
        private Task send = null;
        private bool exit = false;

        public Client()
        {
            InitializeComponent();
        }

        // Метод для вывода сообщений в лог (textbox)
        private void Log(string msg = "")
        {
            if (!exit)
            {
                logTextBox.Invoke((MethodInvoker)delegate
                {
                    if (msg.Length > 0)
                    {
                        logTextBox.AppendText(string.Format("[ {0} ] {1}{2}", DateTime.Now.ToString("HH:mm"), msg, Environment.NewLine));
                    }
                    else
                    {
                        logTextBox.Clear();
                    }
                });
            }
        }

        // Метод для форматирования сообщений об ошибке
        private string ErrorMsg(string msg)
        {
            return string.Format("ERROR: {0}", msg);
        }

        // Метод для форматирования системных сообщений
        private string SystemMsg(string msg)
        {
            return string.Format("SYSTEM: {0}", msg);
        }

        // Метод для управления состоянием подключения (включено/выключено)
        private void Connected(bool status)
        {
            if (!exit)
            {
                connectButton.Invoke((MethodInvoker)delegate
                {
                    connected = status;
                    if (status)
                    {
                        addrTextBox.Enabled = false;
                        portTextBox.Enabled = false;
                        usernameTextBox.Enabled = false;
                        keyTextBox.Enabled = false;
                        connectButton.Text = "Disconnect";
                        Log(SystemMsg("You are now connected"));
                    }
                    else
                    {
                        addrTextBox.Enabled = true;
                        portTextBox.Enabled = true;
                        usernameTextBox.Enabled = true;
                        keyTextBox.Enabled = true;
                        connectButton.Text = "Connect";
                        Log(SystemMsg("You are now disconnected"));
                    }
                });
            }
        }

        // Метод для асинхронного чтения данных из потока клиента
        private void Read(IAsyncResult result)
        {
            int bytes = 0;

            // Проверяем, подключен ли клиент
            if (obj.client.Connected)
            {
                try
                {
                    // Завершаем асинхронную операцию чтения и получаем количество прочитанных байт
                    bytes = obj.stream.EndRead(result);
                }
                catch (Exception ex)
                {
                    // Обрабатываем любые исключения, возникшие во время операции чтения
                    Log(ErrorMsg(ex.Message));
                }
            }

            // Если байты были прочитаны
            if (bytes > 0)
            {
                // Добавляем прочитанные данные к объекту StringBuilder с именем 'data'
                obj.data.AppendFormat("{0}", Encoding.UTF8.GetString(obj.buffer, 0, bytes));

                try
                {
                    // Проверяем, есть ли еще данные для чтения
                    if (obj.stream.DataAvailable)
                    {
                        // Если да, инициируем еще одну асинхронную операцию чтения
                        obj.stream.BeginRead(obj.buffer, 0, obj.buffer.Length, new AsyncCallback(Read), null);
                    }
                    else
                    {
                        // Если данных больше нет, логируем накопленные данные, очищаем объект StringBuilder 'data' и уведомляем об этом
                        Log(obj.data.ToString());
                        obj.data.Clear();
                        obj.handle.Set();
                    }
                }
                catch (Exception ex)
                {
                    // Обрабатываем исключения, возникшие при проверке наличия дополнительных данных или при инициации нового чтения
                    obj.data.Clear();
                    Log(ErrorMsg(ex.Message));
                    obj.handle.Set();
                }
            }
            else
            {
                // Если байты не были прочитаны, это может указывать на отключение клиента; закрываем клиент и уведомляем об этом
                obj.client.Close();
                obj.handle.Set();
            }
        }


        // Метод для асинхронного чтения данных после авторизации
        private void ReadAuth(IAsyncResult result)
        {
            int bytes = 0;

            // Проверяем, подключен ли клиент
            if (obj.client.Connected)
            {
                try
                {
                    // Завершаем асинхронную операцию чтения и получаем количество прочитанных байт
                    bytes = obj.stream.EndRead(result);
                }
                catch (Exception ex)
                {
                    // Обрабатываем любые исключения, возникшие во время операции чтения
                    Log(ErrorMsg(ex.Message));
                }
            }

            // Если байты были прочитаны
            if (bytes > 0)
            {
                // Добавляем прочитанные данные к объекту StringBuilder с именем 'data'
                obj.data.AppendFormat("{0}", Encoding.UTF8.GetString(obj.buffer, 0, bytes));

                try
                {
                    // Проверяем, есть ли еще данные для чтения
                    if (obj.stream.DataAvailable)
                    {
                        // Если да, инициируем еще одну асинхронную операцию чтения
                        obj.stream.BeginRead(obj.buffer, 0, obj.buffer.Length, new AsyncCallback(ReadAuth), null);
                    }
                    else
                    {
                        // Десериализация JSON-данных для проверки статуса авторизации
                        JavaScriptSerializer json = new JavaScriptSerializer();
                        Dictionary<string, string> data = json.Deserialize<Dictionary<string, string>>(obj.data.ToString());

                        // Если статус "authorized", устанавливаем состояние подключения
                        if (data.ContainsKey("status") && data["status"].Equals("authorized"))
                        {
                            Connected(true);
                        }

                        // Очищаем объект StringBuilder 'data' и уведомляем об этом
                        obj.data.Clear();
                        obj.handle.Set();
                    }
                }
                catch (Exception ex)
                {
                    // Обрабатываем исключения, возникшие при проверке наличия дополнительных данных, десериализации или при инициации нового чтения
                    obj.data.Clear();
                    Log(ErrorMsg(ex.Message));
                    obj.handle.Set();
                }
            }
            else
            {
                // Если байты не были прочитаны, это может указывать на отключение клиента; закрываем клиент и уведомляем об этом
                obj.client.Close();
                obj.handle.Set();
            }
        }


        // Метод для авторизации
        private bool Authorize()
        {
            bool success = false;
            Dictionary<string, string> data = new Dictionary<string, string>();

            // Создаем словарь с данными для авторизации (имя пользователя и ключ)
            data.Add("username", obj.username);
            data.Add("key", obj.key);

            // Используем JavaScriptSerializer для сериализации данных в формат JSON
            JavaScriptSerializer json = new JavaScriptSerializer();

            // Отправляем сериализованные данные на сервер
            Send(json.Serialize(data));

            // Цикл ожидания ответа от сервера
            while (obj.client.Connected)
            {
                try
                {
                    // Инициируем асинхронное чтение данных, передавая метод ReadAuth в качестве обратного вызова
                    obj.stream.BeginRead(obj.buffer, 0, obj.buffer.Length, new AsyncCallback(ReadAuth), null);

                    // Ожидаем сигнала от обработчика (возможно, после завершения асинхронного чтения)
                    obj.handle.WaitOne();

                    // Если подключение установлено в результате авторизации, устанавливаем флаг успеха и выходим из цикла
                    if (connected)
                    {
                        success = true;
                        break;
                    }
                }
                catch (Exception ex)
                {
                    // Обрабатываем исключения, возникающие при асинхронном чтении или ожидании сигнала
                    Log(ErrorMsg(ex.Message));
                }
            }

            // Если после цикла подключение так и не установлено, логируем сообщение о неудачной авторизации
            if (!connected)
            {
                Log(SystemMsg("Unauthorized"));
            }

            // Возвращаем флаг успеха авторизации
            return success;
        }


        // Метод для установления соединения с сервером
        private void Connection(IPAddress ip, int port, string username, string key)
        {
            try
            {
                // Создаем новый объект MyClient
                obj = new MyClient();
                obj.username = username;
                obj.key = key;

                // Создаем новый клиент TcpClient и подключаемся к серверу
                obj.client = new TcpClient();
                obj.client.Connect(ip, port);

                // Получаем сетевой поток для чтения и записи данных
                obj.stream = obj.client.GetStream();

                // Инициализируем буфер для чтения данных
                obj.buffer = new byte[obj.client.ReceiveBufferSize];

                // Создаем объект StringBuilder для накопления данных
                obj.data = new StringBuilder();

                // Создаем объект EventWaitHandle для синхронизации потоков
                obj.handle = new EventWaitHandle(false, EventResetMode.AutoReset);

                // Пытаемся авторизоваться
                if (Authorize())
                {
                    // Если успешно авторизованы, запускаем цикл асинхронного чтения данных от сервера
                    while (obj.client.Connected)
                    {
                        try
                        {
                            // Инициируем асинхронное чтение данных
                            obj.stream.BeginRead(obj.buffer, 0, obj.buffer.Length, new AsyncCallback(Read), null);

                            // Ожидаем сигнал от обработчика (возможно, после завершения асинхронного чтения)
                            obj.handle.WaitOne();
                        }
                        catch (Exception ex)
                        {
                            // Обрабатываем исключения, которые могут возникнуть при асинхронном чтении или ожидании сигнала
                            Log(ErrorMsg(ex.Message));
                        }
                    }

                    // После завершения цикла закрываем клиент и устанавливаем состояние подключения как отключено
                    obj.client.Close();
                    Connected(false);
                }
            }
            catch (Exception ex)
            {
                // Обрабатываем исключения, которые могут возникнуть при установлении соединения
                Log(ErrorMsg(ex.Message));
            }
        }


        // Обработчик события нажатия кнопки подключения/отключения
        private void ConnectButton_Click(object sender, EventArgs e)
        {
            if (connected)
            {
                obj.client.Close();
            }
            else if (client == null || !client.IsAlive)
            {
                string address = addrTextBox.Text.Trim();
                string number = portTextBox.Text.Trim();
                string username = usernameTextBox.Text.Trim();
                bool error = false;
                IPAddress ip = null;
                if (address.Length < 1)
                {
                    error = true;
                    Log(SystemMsg("Address is required"));
                }
                else
                {
                    try
                    {
                        ip = Dns.Resolve(address).AddressList[0];
                    }
                    catch
                    {
                        error = true;
                        Log(SystemMsg("Address is not valid"));
                    }
                }
                int port = -1;
                if (number.Length < 1)
                {
                    error = true;
                    Log(SystemMsg("Port number is required"));
                }
                else if (!int.TryParse(number, out port))
                {
                    error = true;
                    Log(SystemMsg("Port number is not valid"));
                }
                else if (port < 0 || port > 65535)
                {
                    error = true;
                    Log(SystemMsg("Port number is out of range"));
                }
                if (username.Length < 1)
                {
                    error = true;
                    Log(SystemMsg("Username is required"));
                }
                if (!error)
                {
                    // encryption key is optional
                    client = new Thread(() => Connection(ip, port, username, keyTextBox.Text))
                    {
                        IsBackground = true
                    };
                    client.Start();
                }
            }
        }

        // Метод для асинхронной записи данных в поток клиента
        private void Write(IAsyncResult result)
        {
            if (obj.client.Connected)
            {
                try
                {
                    obj.stream.EndWrite(result);
                }
                catch (Exception ex)
                {
                    Log(ErrorMsg(ex.Message));
                }
            }
        }

        // Метод для начала асинхронной записи сообщения в поток клиента
        private void BeginWrite(string msg)
        {
            byte[] buffer = Encoding.UTF8.GetBytes(msg);
            if (obj.client.Connected)
            {
                try
                {
                    obj.stream.BeginWrite(buffer, 0, buffer.Length, new AsyncCallback(Write), null);
                }
                catch (Exception ex)
                {
                    Log(ErrorMsg(ex.Message));
                }
            }
        }

        // Метод для отправки сообщения
        private void Send(string msg)
        {
            if (send == null || send.IsCompleted)
            {
                send = Task.Factory.StartNew(() => BeginWrite(msg));
            }
            else
            {
                send.ContinueWith(antecendent => BeginWrite(msg));
            }
        }

        // Обработчик события нажатия клавиши "Enter" в текстовом поле для ввода сообщения
        private void SendTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                if (sendTextBox.Text.Length > 0)
                {
                    string msg = sendTextBox.Text;
                    sendTextBox.Clear();
                    Log(string.Format("{0} (You): {1}", obj.username, msg));
                    if (connected)
                    {
                        Send(msg);
                    }
                }
            }
        }

        // Обработчик события закрытия формы
        private void Client_FormClosing(object sender, FormClosingEventArgs e)
        {
            exit = true;
            if (connected)
            {
                obj.client.Close();
            }
        }

        // Обработчик события нажатия кнопки очистки лога
        private void ClearButton_Click(object sender, EventArgs e)
        {
            Log();
        }


        private void CheckBox_CheckedChanged(object sender, EventArgs e)
        {
            if (keyTextBox.PasswordChar == '*')
            {
                keyTextBox.PasswordChar = '\0';
            }
            else
            {
                keyTextBox.PasswordChar = '*';
            }
        }
    }
}
