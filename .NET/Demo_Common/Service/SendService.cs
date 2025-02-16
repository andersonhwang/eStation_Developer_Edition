﻿using Demo_Common.Entity;
using Demo_Common.Enum;
using MessagePack;
using Serilog;
using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;

namespace Demo_Common.Service
{
    /// <summary>
    /// Send service
    /// </summary>
    public class SendService
    {
        private readonly ConcurrentQueue<ApData> RecvQueue = [];
        private readonly Dictionary<string, Ap> Clients = [];
        private MQTT mqtt;
        private static SendService instance = new();
        /// <summary>
        /// Send service instance
        /// </summary>
        public static SendService Instance => instance;

        /// <summary>
        /// AP status delegate
        /// </summary>
        /// <param name="status">AP status</param>
        public delegate void ApStatusDelegate(string id, ApStatus status);
        public event ApStatusDelegate? ApStatusHandler;
        /// <summary>
        /// AP infor delegate
        /// </summary>
        /// <param name="infor">AP information</param>
        public delegate void ApInforDelegate(ApInfor infor);
        public event ApInforDelegate? ApInforHandler;
        /// <summary>
        /// AP heartbeat delegate
        /// </summary>
        /// <param name="heartbeat">AP heartbeat</param>
        public delegate void ApHeartbeatDelegate(ApHeartbeat heartbeat);
        public event ApHeartbeatDelegate? ApHeartbeatHandler;
        /// <summary>
        /// Task response delegate
        /// </summary>
        /// <param name="response">Task response</param>
        public delegate void ApMessageDelegate(ApMessage message);
        public event ApMessageDelegate? ApMessageHandler;
        /// <summary>
        /// Task result delegate
        /// </summary>
        /// <param name="result"></param>
        public delegate void TaskResultDelegate(TaskResult result);
        public event TaskResultDelegate? TaskResultHandler;
        /// <summary>
        /// Debug request delegate
        /// </summary>
        /// <param name="item">Debug item</param>
        public delegate void DebugRequestDelegate(DebugItem item);
        public event DebugRequestDelegate? DebugRequestHandler;
        /// <summary>
        /// Debug response delegate
        /// </summary>
        /// <param name="item">Debug item</param>
        public delegate void DebugResponseDelegate(DebugItem item);
        public event DebugResponseDelegate? DebugResponseHandler;

        /// <summary>
        /// Run send service
        /// </summary>
        /// <param name="conn">Connection information</param>
        public bool Run(ConnInfo conn)
        {
            try
            {
                Task.Run(async () =>
                {
                    while (true)
                    {
                        try
                        {
                            if (!RecvQueue.TryDequeue(out var item))
                            {
                                await Task.Delay(200);
                                continue;
                            }

                            var json = string.Empty;
                            switch (item.TopicAlias)
                            {
                                case 0x80:
                                    var infor = MessagePackSerializer.Deserialize<ApInfor>(item.Data);
                                    if (infor is null) continue;
                                    json = JsonSerializer.Serialize(infor);
                                    UpdateInfor(item.Id, infor);
                                    ApInforHandler?.Invoke(infor);
                                    ApStatusHandler?.Invoke(item.Id, ApStatus.Online);
                                    break;
                                case 0x81:
                                    var message = MessagePackSerializer.Deserialize<ApMessage>(item.Data);
                                    if (message is null) continue;
                                    json = JsonSerializer.Serialize(message);
                                    ApMessageHandler?.Invoke(message);
                                    break;
                                case 0x82:
                                    var result = MessagePackSerializer.Deserialize<TaskResult>(item.Data);
                                    if (result is null) continue;
                                    json = JsonSerializer.Serialize(result);
                                    TaskResultHandler?.Invoke(result);
                                    break;
                                case 0x83:
                                    var heartbeat = MessagePackSerializer.Deserialize<ApHeartbeat>(item.Data);
                                    if (heartbeat is null) continue;
                                    json = JsonSerializer.Serialize(heartbeat);
                                    ApHeartbeatHandler?.Invoke(heartbeat);
                                    break;
                                default: 
                                    break;
                            }
                            DebugResponseHandler?.Invoke(new DebugItem(item.TopicAlias, item.Topic, json));
                        }
                        catch
                        {
                            // Not important
                        }
                    }
                });

                mqtt = new MQTT(conn, ProcessApStatus, ProcessApData, AddClient);
                return mqtt.Run();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Send_Service_Error");
                return false;
            }
        }

        /// <summary>
        /// Register AP status event
        /// </summary>
        /// <param name="status"></param>
        public void Register(ApStatusDelegate status) => ApStatusHandler += status;

        /// <summary>
        /// Register AP information event
        /// </summary>
        /// <param name="infor">AP status event</param>
        public void Register(ApInforDelegate infor) => ApInforHandler += infor;

        /// <summary>
        /// Register AP heartbeat event
        /// </summary>
        /// <param name="heartbeat">AP data event</param>
        public void Register(ApHeartbeatDelegate heartbeat) => ApHeartbeatHandler += heartbeat;

        /// <summary>
        /// Register task response event
        /// </summary>
        /// <param name="response">Task response event</param>
        public void Register(ApMessageDelegate message) => ApMessageHandler += message;

        /// <summary>
        /// Register task result event
        /// </summary>
        /// <param name="result">Task result event</param>
        public void Register(TaskResultDelegate result) => TaskResultHandler += result;

        /// <summary>
        /// Register debug response event
        /// </summary>
        /// <param name="debug">Debug response event</param>
        public void Register(DebugResponseDelegate debug) => DebugResponseHandler += debug;

        /// <summary>
        /// Register debug request event
        /// </summary>
        /// <param name="debug">Debug request event</param>
        public void Register(DebugRequestDelegate debug) => DebugRequestHandler += debug;

        /// <summary>
        /// AP status handler
        /// </summary>
        /// <param name="id">Client ID</param>
        /// <param name="status">AP status</param>
        public void ProcessApStatus(string id, ApStatus status)
        {
            ApStatusHandler?.Invoke(id, status);

            if (Clients.TryGetValue(id, out Ap? ap))
            {
                ap.Status = status;
                switch (ap.Status)
                {
                    case ApStatus.Online:
                        ap.Status = ApStatus.Online;
                        ap.ConnectTime = DateTime.Now;
                        break;
                    case ApStatus.Offline:
                        ap.Status = ApStatus.Offline;
                        ap.DisconnectTime = DateTime.Now;
                        break;
                    case ApStatus.Working:
                        ap.Status = ApStatus.Working;
                        ap.SendTime = DateTime.Now;
                        break;
                    case ApStatus.Connecting:
                        ap.Status = ApStatus.Connecting;
                        break;
                }
            }
        }

        /// <summary>
        /// AP data handler
        /// </summary>
        /// <param name="data">AP data</param>
        public void ProcessApData(ApData data)
        {
            RecvQueue.Enqueue(data);
        }

        /// <summary>
        /// Add client
        /// </summary>
        /// <param name="id">AP ID</param>
        /// <param name="endPoint">EndPoint</param>
        public void AddClient(string id, EndPoint endPoint)
        {
            if (Clients.ContainsKey(id)) return;
            Clients.Add(id, new Ap(id, endPoint));
        }

        /// <summary>
        /// Send data
        /// </summary>
        /// <typeparam name="T">Data type</typeparam>
        /// <param name="id">AP ID</param>
        /// <param name="alias">Topic alias</param>
        /// <param name="topic">Topic</param>
        /// <param name="t">Data object</param>
        /// <returns>Send result</returns>
        public async Task<SendResult> Send<T>(string id, ushort alias, string topic, T t)
        {
            if (!Clients.ContainsKey(id)) return SendResult.NotExist;
            var client = Clients[id];
            if (client.Status != ApStatus.Online) return SendResult.Offline;

            DebugRequestHandler?.Invoke(new DebugItem(alias, topic, JsonSerializer.Serialize(t)));
            return await mqtt.Send(alias, topic, t);
        }

        /// <summary>
        /// Update AP infor
        /// </summary>
        /// <param name="id">AP ID</param>
        /// <param name="infor">AP infor</param>
        public void UpdateInfor(string id, ApInfor infor)
        {
            if (Clients.TryGetValue(id, out Ap? ap))
            {
                ap.Infor = infor;
            }
        }
    }
}
