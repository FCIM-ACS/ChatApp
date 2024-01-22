using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace Server
{
    public partial class Server : Form
    {
        // Флаг, указывающий, активен ли сервер
        private bool active = false;
        // Поток, отвечающий за прослушивание входящих соединений
        private Thread listener = null;
        // Уникальный идентификатор для клиентов
        private long id = 0;
        // Структура, представляющая подключенного клиента
        private struct MyClient
        {
            public long id;
            public StringBuilder username;
            public TcpClient client;
            public NetworkStream stream;
            public byte[] buffer;
            public StringBuilder data;
            public EventWaitHandle handle;
        };
        // Коллекция для хранения подключенных клиентов по их уникальным идентификаторам
        private ConcurrentDictionary<long, MyClient> clients = new ConcurrentDictionary<long, MyClient>();
        // Задача для отправки сообщений клиентам
        private Task send = null;
        // Поток для обработки отключения клиентов
        private Thread disconnect = null;
        // Флаг для завершения работы сервера
        private bool exit = false;
        public Server()
        {
            InitializeComponent();
        }
        // Метод для логирования событий сервера
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
        // Метод для управления активностью сервера
        private void Active(bool status)
        {
            if (!exit)
            {
                startButton.Invoke((MethodInvoker)delegate
                {
                    active = status;
                    if (status)
                    {
                        addrTextBox.Enabled = false;
                        portTextBox.Enabled = false;
                        usernameTextBox.Enabled = false;
                        keyTextBox.Enabled = false;
                        startButton.Text = "Stop";
                        Log(SystemMsg("Server has started"));
                    }
                    else
                    {
                        addrTextBox.Enabled = true;
                        portTextBox.Enabled = true;
                        usernameTextBox.Enabled = true;
                        keyTextBox.Enabled = true;
                        startButton.Text = "Start";
                        Log(SystemMsg("Server has stopped"));
                    }
                });
            }
        }


        private void AddToGrid(long id, string name)
        {
            if (!exit)
            {
                clientsDataGridView.Invoke((MethodInvoker)delegate
                {
                    // Создаем строку данных для DataGridView
                    string[] row = new string[] { id.ToString(), name };
                    // Добавляем строку в DataGridView
                    clientsDataGridView.Rows.Add(row);
                    // Обновляем текстовую метку с общим количеством клиентов
                    totalLabel.Text = string.Format("Total clients: {0}", clientsDataGridView.Rows.Count);
                });
            }
        }

        // Метод для удаления клиента из DataGridView на форме
        private void RemoveFromGrid(long id)
        {
            if (!exit)
            {
                clientsDataGridView.Invoke((MethodInvoker)delegate
                {
                    // Итерируем по строкам DataGridView, чтобы найти нужного клиента
                    foreach (DataGridViewRow row in clientsDataGridView.Rows)
                    {
                        if (row.Cells["identifier"].Value.ToString() == id.ToString())
                        {
                            // Удаляем строку, соответствующую клиенту
                            clientsDataGridView.Rows.RemoveAt(row.Index);
                            break;
                        }
                    }
                    // Обновляем текстовую метку с общим количеством клиентов
                    totalLabel.Text = string.Format("Total clients: {0}", clientsDataGridView.Rows.Count);
                });
            }
        }

        private void Read(IAsyncResult result)
        {
            // Получение объекта MyClient из асинхронного результата
            MyClient obj = (MyClient)result.AsyncState;
            int bytes = 0;

            // Проверка, подключен ли клиент
            if (obj.client.Connected)
            {
                try
                {
                    // Завершение асинхронного чтения данных
                    bytes = obj.stream.EndRead(result);
                }
                catch (Exception ex)
                {
                    // Обработка и логирование ошибок чтения
                    Log(ErrorMsg(ex.Message));
                }
            }
            // Проверка, были ли получены байты
            if (bytes > 0)
            {
                // Добавление полученных байт в буфер данных объекта MyClient
                obj.data.AppendFormat("{0}", Encoding.UTF8.GetString(obj.buffer, 0, bytes));

                try
                {
                    // Проверка, есть ли еще данные для чтения
                    if (obj.stream.DataAvailable)
                    {
                        // Если есть, начать новое асинхронное чтение
                        obj.stream.BeginRead(obj.buffer, 0, obj.buffer.Length, new AsyncCallback(Read), obj);
                    }
                    else
                    {
                        // Если данных больше нет, формирование сообщения и отправка
                        string msg = string.Format("{0}: {1}", obj.username, obj.data);
                        Log(msg);
                        Send(msg, obj.id);
                        obj.data.Clear();
                        obj.handle.Set();
                    }
                }
                catch (Exception ex)
                {
                    // Обработка и логирование ошибок обработки данных
                    obj.data.Clear();
                    Log(ErrorMsg(ex.Message));
                    obj.handle.Set();
                }
            }
            else
            {
                // Если байты не были получены, закрытие соединения и установка события
                obj.client.Close();
                obj.handle.Set();
            }
        }

        private void ReadAuth(IAsyncResult result)
        {
            MyClient obj = (MyClient)result.AsyncState;
            int bytes = 0;
            // Проверка, подключен ли клиент
            if (obj.client.Connected)
            {
                try
                {
                    // Завершение асинхронного чтения данных
                    bytes = obj.stream.EndRead(result);
                }
                catch (Exception ex)
                {
                    // Обработка и логирование ошибок чтения
                    Log(ErrorMsg(ex.Message));
                }
            }
            // Проверка, были ли получены байты
            if (bytes > 0)
            {
                // Добавление полученных байт в буфер данных объекта MyClient
                obj.data.AppendFormat("{0}", Encoding.UTF8.GetString(obj.buffer, 0, bytes));

                try
                {
                    // Проверка, есть ли еще данные для чтения
                    if (obj.stream.DataAvailable)
                    {
                        // Если есть, начать новое асинхронное чтение
                        obj.stream.BeginRead(obj.buffer, 0, obj.buffer.Length, new AsyncCallback(ReadAuth), obj);
                    }
                    else
                    {
                        // Десериализация JSON-данных для авторизации
                        JavaScriptSerializer json = new JavaScriptSerializer();
                        Dictionary<string, string> data = json.Deserialize<Dictionary<string, string>>(obj.data.ToString());

                        // Проверка данных для авторизации
                        if (!data.ContainsKey("username") || data["username"].Length < 1 || !data.ContainsKey("key") || !data["key"].Equals(keyTextBox.Text))
                        {
                            // Если авторизация не прошла успешно, закрытие соединения
                            obj.client.Close();
                        }
                        else
                        {
                            // Если авторизация успешна, установка имени пользователя и отправка сообщения об авторизации
                            obj.username.Append(data["username"].Length > 200 ? data["username"].Substring(0, 200) : data["username"]);
                            Send("{\"status\": \"authorized\"}", obj);
                        }
                        obj.data.Clear();
                        obj.handle.Set();
                    }
                }
                catch (Exception ex)
                {
                    // Обработка и логирование ошибок обработки данных
                    obj.data.Clear();
                    Log(ErrorMsg(ex.Message));
                    obj.handle.Set();
                }
            }
            else
            {
                // Если байты не были получены, закрытие соединения и установка события
                obj.client.Close();
                obj.handle.Set();
            }
        }


        private bool Authorize(MyClient obj)
        {
            bool success = false;
            // Повторять попытки авторизации, пока клиент подключен
            while (obj.client.Connected)
            {
                try
                {
                    // Начать асинхронное чтение для авторизации
                    obj.stream.BeginRead(obj.buffer, 0, obj.buffer.Length, new AsyncCallback(ReadAuth), obj);
                    obj.handle.WaitOne();

                    // Если имя пользователя установлено, авторизация успешна
                    if (obj.username.Length > 0)
                    {
                        success = true;
                        break;
                    }
                }
                catch (Exception ex)
                {
                    // Обработка и логирование ошибок авторизации
                    Log(ErrorMsg(ex.Message));
                }
            }
            return success;
        }
        // Метод для обработки нового подключения
        private void Connection(MyClient obj)
        {
            // Если клиент авторизован успешно
            if (Authorize(obj))
            {
                // Добавляем клиента в коллекцию и отображаем на форме
                clients.TryAdd(obj.id, obj);
                AddToGrid(obj.id, obj.username.ToString());
                string msg = string.Format("{0} has connected", obj.username);
                Log(SystemMsg(msg));
                // Отправляем уведомление о подключении всем клиентам
                Send(SystemMsg(msg), obj.id);

                // Ожидаем новых сообщений от клиента
                while (obj.client.Connected)
                {
                    try
                    {
                        obj.stream.BeginRead(obj.buffer, 0, obj.buffer.Length, new AsyncCallback(Read), obj);
                        obj.handle.WaitOne();
                    }
                    catch (Exception ex)
                    {
                        Log(ErrorMsg(ex.Message));
                    }
                }

                // Когда клиент отключается, закрываем соединение и удаляем из коллекции
                obj.client.Close();
                clients.TryRemove(obj.id, out MyClient tmp);
                RemoveFromGrid(tmp.id);
                msg = string.Format("{0} has disconnected", tmp.username);
                Log(SystemMsg(msg));
                // Отправляем уведомление об отключении всем клиентам
                Send(msg, tmp.id);
            }
        }
        // Метод для запуска слушателя входящих подключений
        private void Listener(IPAddress ip, int port)
        {
            TcpListener listener = null;
            try
            {
                // Создаем и запускаем TcpListener
                listener = new TcpListener(ip, port);
                listener.Start();
                Active(true);

                // Бесконечный цикл прослушивания входящих подключений
                while (active)
                {
                    // Проверяем, есть ли в очереди ожидания новые подключения
                    if (listener.Pending())
                    {
                        try
                        {
                            // Создаем объект MyClient для нового подключения
                            MyClient obj = new MyClient();
                            obj.id = id;
                            obj.username = new StringBuilder();
                            obj.client = listener.AcceptTcpClient();
                            obj.stream = obj.client.GetStream();
                            obj.buffer = new byte[obj.client.ReceiveBufferSize];
                            obj.data = new StringBuilder();
                            obj.handle = new EventWaitHandle(false, EventResetMode.AutoReset);

                            // Запускаем новый поток для обработки соединения
                            Thread th = new Thread(() => Connection(obj))
                            {
                                IsBackground = true
                            };
                            th.Start();

                            // Увеличиваем идентификатор для следующего клиента
                            id++;
                        }
                        catch (Exception ex)
                        {
                            // Логируем ошибку, если что-то идет не так при принятии нового подключения
                            Log(ErrorMsg(ex.Message));
                        }
                    }
                    else
                    {
                        // Если новых подключений нет, спим на некоторое время (например, 500 миллисекунд)
                        Thread.Sleep(500);
                    }
                }
                // Когда цикл завершен (например, сервер остановлен), устанавливаем статус активности в false
                Active(false);
            }
            catch (Exception ex)
            {
                // Если произошла ошибка при запуске TcpListener, логируем ее
                Log(ErrorMsg(ex.Message));
            }
            finally
            {
                // В любом случае, закрываем сервер, если он был создан
                if (listener != null)
                {
                    listener.Server.Close();
                }
            }
        }
        private void StartButton_Click(object sender, EventArgs e)
        {
            // При нажатии кнопки Start/Stop
            if (active)
            {
                // Если сервер активен, выключаем его
                active = false;
            }
            else if (listener == null || !listener.IsAlive)
            {
                // Если сервер не активен и не запущен поток прослушивания

                // Получение параметров для запуска сервера из текстовых полей формы
                string address = addrTextBox.Text.Trim();
                string number = portTextBox.Text.Trim();
                string username = usernameTextBox.Text.Trim();
                bool error = false;
                IPAddress ip = null;

                // Проверка введенного IP-адреса
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

                // Проверка введенного порта
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

                // Проверка введенного имени пользователя
                if (username.Length < 1)
                {
                    error = true;
                    Log(SystemMsg("Username is required"));
                }

                // Если ошибок не обнаружено, запускаем поток прослушивания
                if (!error)
                {
                    listener = new Thread(() => Listener(ip, port))
                    {
                        IsBackground = true
                    };
                    listener.Start();
                }
            }
        }

        private void Write(IAsyncResult result)
        {
            // Метод для завершения асинхронной записи данных в поток клиента
            MyClient obj = (MyClient)result.AsyncState;
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

        private void BeginWrite(string msg, MyClient obj)
        {
            // Метод для начала асинхронной записи сообщения в поток клиента
            byte[] buffer = Encoding.UTF8.GetBytes(msg);
            if (obj.client.Connected)
            {
                try
                {
                    obj.stream.BeginWrite(buffer, 0, buffer.Length, new AsyncCallback(Write), obj);
                }
                catch (Exception ex)
                {
                    Log(ErrorMsg(ex.Message));
                }
            }
        }

        private void BeginWrite(string msg, long id = -1)
        {
            // Метод для начала асинхронной записи сообщения в поток всех клиентов, кроме отправителя
            byte[] buffer = Encoding.UTF8.GetBytes(msg);
            foreach (KeyValuePair<long, MyClient> obj in clients)
            {
                if (id != obj.Value.id && obj.Value.client.Connected)
                {
                    try
                    {
                        obj.Value.stream.BeginWrite(buffer, 0, buffer.Length, new AsyncCallback(Write), obj.Value);
                    }
                    catch (Exception ex)
                    {
                        Log(ErrorMsg(ex.Message));
                    }
                }
            }
        }

        private void Send(string msg, MyClient obj)
        {
            // Метод для отправки сообщения конкретному клиенту
            if (send == null || send.IsCompleted)
            {
                send = Task.Factory.StartNew(() => BeginWrite(msg, obj));
            }
            else
            {
                send.ContinueWith(antecedent => BeginWrite(msg, obj));
            }
        }

        private void Send(string msg, long id = -1)
        {
            // Метод для отправки сообщения всем клиентам, кроме отправителя
            if (send == null || send.IsCompleted)
            {
                send = Task.Factory.StartNew(() => BeginWrite(msg, id));
            }
            else
            {
                send.ContinueWith(antecedent => BeginWrite(msg, id));
            }
        }

        private void SendTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            // Обработчик события нажатия клавиши Enter в поле ввода сообщения
            if (e.KeyCode == Keys.Enter)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                if (sendTextBox.Text.Length > 0)
                {
                    // Отправка сообщения при нажатии Enter
                    string msg = sendTextBox.Text;
                    sendTextBox.Clear();
                    Log(string.Format("{0} (You): {1}", usernameTextBox.Text.Trim(), msg));
                    Send(string.Format("{0}: {1}", usernameTextBox.Text.Trim(), msg));
                }
            }
        }

        private void Disconnect(long id = -1)
        {
            // Метод для отключения клиентов
            if (disconnect == null || !disconnect.IsAlive)
            {
                disconnect = new Thread(() =>
                {
                    if (id >= 0)
                    {
                        // Если передан ID клиента, отключаем только его
                        clients.TryGetValue(id, out MyClient obj);
                        obj.client.Close();
                        RemoveFromGrid(obj.id);
                    }
                    else
                    {
                        // Иначе отключаем всех клиентов
                        foreach (KeyValuePair<long, MyClient> obj in clients)
                        {
                            obj.Value.client.Close();
                            RemoveFromGrid(obj.Value.id);
                        }
                    }
                })
                {
                    IsBackground = true
                };
                disconnect.Start();
            }
        }

        private void DisconnectButton_Click(object sender, EventArgs e)
        {
            // Обработчик события нажатия кнопки отключения
            Disconnect();
        }

        private void Server_FormClosing(object sender, FormClosingEventArgs e)
        {
            // Обработчик события закрытия формы
            exit = true;
            active = false;
            Disconnect();
        }

        private void ClientsDataGridView_CellClick(object sender, DataGridViewCellEventArgs e)
        {
            // Обработчик события клика по ячейке в DataGridView
            if (e.RowIndex >= 0 && e.ColumnIndex == clientsDataGridView.Columns["dc"].Index)
            {
                long.TryParse(clientsDataGridView.Rows[e.RowIndex].Cells["identifier"].Value.ToString(), out long id);
                Disconnect(id);
            }
        }

        private void ClearButton_Click(object sender, EventArgs e)
        {
            // Обработчик события нажатия кнопки очистки лога
            Log();
        }

        private void CheckBox_CheckedChanged(object sender, EventArgs e)
        {
            // Обработчик события изменения состояния чекбокса для отображения/скрытия пароля
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

