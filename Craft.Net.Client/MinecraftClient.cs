﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Craft.Net.Client.Handlers;
using Craft.Net.Data;

namespace Craft.Net.Client
{
    public partial class MinecraftClient
    {
        static MinecraftClient()
        {
            PacketHandlerDelegates = new PacketHandler[256];
            PacketHandlers.RegisterHandlers();
        }

        public delegate void PacketHandler(MinecraftClient client, IPacket packet);
        private static PacketHandler[] PacketHandlerDelegates { get; set; }

        public static void RegisterPacketHandler(byte packetId, PacketHandler handler)
        {
            PacketHandlerDelegates[packetId] = handler;
        }

        public MinecraftClient(Session session)
        {
            Session = session;
            SendQueue = new ConcurrentQueue<IPacket>();
        }

        public void Connect(string address)
        {
            Connect(ParseEndPoint(address));
        }

        public static IPEndPoint ParseEndPoint(string endpoint)
        {
            IPAddress address;
            int port;
            if (endpoint.Contains(':'))
            {
                // Both IP and port are specified
                var parts = endpoint.Split(':');
                if (!IPAddress.TryParse(parts[0], out address))
                    address = Resolve(parts[0]);
                return new IPEndPoint(address, int.Parse(parts[1]));
            }
            if (IPAddress.TryParse(endpoint, out address))
                return new IPEndPoint(address, 25565);
            if (int.TryParse(endpoint, out port))
                return new IPEndPoint(IPAddress.Loopback, port);
            return new IPEndPoint(Resolve(endpoint), 25565);
        }

        private static IPAddress Resolve(string arg)
        {
            return Dns.GetHostEntry(arg).AddressList.FirstOrDefault();
        }

        public void Connect(IPEndPoint endPoint)
        {
            if (Client != null && Client.Connected)
                throw new InvalidOperationException("Already connected to a server!");
            EndPoint = endPoint;
            Client = new TcpClient(EndPoint.AddressFamily);
            Client.Connect(EndPoint);
            NetworkStream = Client.GetStream();
            Stream = new MinecraftStream(new BufferedStream(NetworkStream));
            NetworkWorkerThread = new Thread(NetworkWorker);
            NetworkWorkerThread.Start();
            var handshake = new HandshakePacket(PacketReader.ProtocolVersion, Session.Username,
                EndPoint.Address.ToString(), EndPoint.Port);
            SendPacket(handshake);
        }

        public void Disconnect(string reason)
        {
            NetworkWorkerThread.Abort();
            if (Client.Connected)
            {
                try
                {
                    new DisconnectPacket(reason).WritePacket(Stream);
                    Stream.Flush();
                    Client.Close();
                }
                catch { }
            }
        }

        public void SendChat(string message)
        {
            SendPacket(new ChatMessagePacket(message));
        }

        public void Respawn()
        {
            if (Health > 0)
                throw new InvalidOperationException("Player is not dead!");
            //SendPacket(new RespawnPacket(Dimension.Overworld, // TODO: Other dimensions
            //    Level.Difficulty, Level.GameMode, World.Height, Level.World.LevelType));
            SendPacket(new ClientStatusPacket(ClientStatusPacket.ClientStatus.Respawn));
        }

        public void SendPacket(IPacket packet)
        {
            SendQueue.Enqueue(packet);
        }

        private DateTime nextPlayerUpdate = DateTime.MinValue;
        private void NetworkWorker()
        {
            while (true)
            {
                if (IsSpawned && nextPlayerUpdate < DateTime.Now)
                {
                    nextPlayerUpdate = DateTime.Now.AddMilliseconds(500);
                    SendPacket(new PlayerPacket(true)); // TODO: Store OnGround properly
                }
                // Send queued packets
                while (SendQueue.Count != 0)
                {
                    IPacket packet;
                    if (SendQueue.TryDequeue(out packet))
                    {
                        try
                        {
                            // Write packet
                            packet.WritePacket(Stream);
#if DEBUG
                            LogProvider.Log(packet, true);
#endif
                            Stream.Flush();
                            // Redundant, because creating an object every time a packet is sent
                            // would be expensive when it's not needed
                            if (PacketSent != null)
                                OnPacketSent(new PacketEventArgs(packet));
                            if (packet is DisconnectPacket)
                                return;
                        }
                        catch { /* TODO */ }
                    }
                }
                // Read incoming packets
                var readTimeout = DateTime.Now.AddMilliseconds(20); // Maximum read time given to server per iteration
                while (NetworkStream.DataAvailable && DateTime.Now < readTimeout)
                {
                    try
                    {
                        var packet = PacketReader.ReadPacket(Stream);
#if DEBUG
                        LogProvider.Log(packet, false);
#endif
                        if (PacketRecieved != null)
                            OnPacketRecieved(new PacketEventArgs(packet));
                        HandlePacket(packet);
                        if (packet is DisconnectPacket)
                            return;
                    }
                    catch { /* TODO */ }
                }
                Thread.Sleep(1);
            }
        }

        private void HandlePacket(IPacket packet)
        {
            if (PacketHandlerDelegates[packet.Id] != null)
                PacketHandlerDelegates[packet.Id](this, packet);
            else
                LogProvider.Log("Warning: No packet handlers for 0x" + packet.Id.ToString("X2"), LogImportance.Low);
        }
    }
}
