using System.Buffers;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Numerics;
using Shared;
using Shared.Packet;
using Shared.Packet.Packets;

namespace Server;

public class Server {
    public readonly List<Client> Clients = new List<Client>();
    public IEnumerable<Client> ClientsConnected => Clients.Where(client => client.Metadata.ContainsKey("lastGamePacket") && client.Connected);
    public readonly Logger Logger = new Logger("Server");
    private readonly MemoryPool<byte> memoryPool = MemoryPool<byte>.Shared;
    public Func<Client, IPacket, bool>? PacketHandler = null!;
    public event Action<Client, ConnectPacket> ClientJoined = null!;

    public async Task GameListen(CancellationToken? token = null) {
        Socket serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        serverSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        serverSocket.Bind(new IPEndPoint(IPAddress.Parse(Settings.Instance.Server.Address), Settings.Instance.Server.GamePort));
        serverSocket.Listen();

        Logger.Info($"Listening on {serverSocket.LocalEndPoint}");

        try {
            while (true) {
                Socket socket = token.HasValue ? await serverSocket.AcceptAsync(token.Value) : await serverSocket.AcceptAsync();
                socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.NoDelay, true);

                Logger.Warn($"Accepted connection for client {socket.RemoteEndPoint}");

                try {
                    Task.Run(() => HandleSocket(socket));
                }
                catch (Exception e) {
                    Logger.Error($"Error occured while setting up socket handler? {e}");
                }
            }
        }
        catch (OperationCanceledException) {
            // ignore the exception, it's just for closing the server

            Logger.Info("Game server closing");

            try {
                serverSocket.Shutdown(SocketShutdown.Both);
            }
            catch {
                // ignored
            }
            finally {
                serverSocket.Close();
            }

            Logger.Info("Game server closed");
        }
    }

    public async Task ChatListen(CancellationToken? token = null) {
		Socket serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0);
		serverSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
		serverSocket.Bind(new IPEndPoint(IPAddress.Parse(Settings.Instance.Server.Address), Settings.Instance.Server.ChatPort));

		Logger.Info($"Listening on {serverSocket.LocalEndPoint} (chat)");

/*		PacketHeader header = new PacketHeader {
			Id = Guid.Empty,
			Type = PacketType.Unknown,
			PacketSize = 0,
		};
		IMemoryOwner<byte> memory = MemoryPool<byte>.Shared.RentZero(Constants.HeaderSize);

		FillPacket(header, new UnhandledPacket(), memory.Memory);
*/
		IPEndPoint clientEP = new IPEndPoint(0, 0);

		byte[] headerBuffer = new Byte[Constants.HeaderSize];

		SocketReceiveFromResult r;

		try {
			while (true) {
				r = await (token == null ? 
					serverSocket.ReceiveFromAsync(((Memory<byte>) headerBuffer)[..Constants.HeaderSize], SocketFlags.None, clientEP)
					: serverSocket.ReceiveFromAsync(((Memory<byte>) headerBuffer)[..Constants.HeaderSize], SocketFlags.None, clientEP, token.Value));
				
				if (r.ReceivedBytes != Constants.HeaderSize)
					break;
				PacketHeader header = GetHeader(((Memory<byte>) headerBuffer).Span);
			
				IMemoryOwner<byte> packetBuf = memoryPool.Rent(header.PacketSize);
				if (await(token == null ? 
					serverSocket.ReceiveAsync(packetBuf.Memory[..header.PacketSize], SocketFlags.None)
					: serverSocket.ReceiveAsync(packetBuf.Memory[..header.PacketSize], SocketFlags.None, token.Value)) != header.PacketSize)
						break;

				switch (header.Type) {

					case PacketType.ChatConnect:
						IPEndPoint? currEP = null;
						string? currIP = null;
						ChatConnectPacket inPacket = new ChatConnectPacket();
						inPacket.Deserialize(packetBuf.Memory.Span[..header.PacketSize]);
						packetBuf.Dispose();
						
						if(r.RemoteEndPoint is IPEndPoint ep) {
							currEP = ep;
							currIP = ep.Address.ToString();
						}

						lock(Clients) {
							//Check if there is a game client with a matching name, and if so, whether it is not already bound to a chat client
							if(Clients.Find(c => c.Name == inPacket.Name && c.Connected) is Client gameClient && ((gameClient.ChatEP is null) || (gameClient.ChatEP.Address.ToString() == currIP))) {
								gameClient.ChatEP = currEP;

								ChatInitPacket outPacket = new ChatInitPacket(Settings.Instance.ProximityChat.SilenceRadius, Settings.Instance.ProximityChat.PeakRadius);

								PacketHeader responseHeader = new PacketHeader {
									Id = gameClient.Id,
									Type = PacketType.ChatInit,
									PacketSize = outPacket.Size,
								};

								IMemoryOwner<byte> memory = memoryPool.Rent(Constants.HeaderSize + outPacket.Size);

								FillPacket(responseHeader, outPacket, memory.Memory);

								SendToAsyncAndDispose(serverSocket, memory, gameClient.ChatEP, token);

								Logger.Info($"New chat client added for {gameClient.Name}");

							}
							
							else Logger.Warn($"New chat client failed to join with name {inPacket.Name}");
						}
						break;
					
					case PacketType.ChatVoice:
						lock(Clients) {
							if(Clients.Find(c => c.Id == header.Id && c.Connected && c.Metadata.ContainsKey("position")) is Client gameClient) {
								Vector3 pos = (Vector3) gameClient.Metadata["position"];

								List<Client> to = Clients.FindAll(c => c.Connected && c.Metadata.ContainsKey("position") && 
										Vector3.Distance((Vector3)c.Metadata["position"]!, pos) < Settings.Instance.ProximityChat.SilenceRadius && c.ChatEP != null && c != gameClient);

								short size = (short) (header.PacketSize + Constants.HeaderSize);
								IMemoryOwner<byte> mem = memoryPool.Rent(to.Count * size);
								for(int i = 0; i < to.Count; i++) {
									(new Memory<byte>(headerBuffer)).CopyTo(mem.Memory[(Index)(i*size)..]);
									Logger.Warn($"{i}");
									packetBuf.Memory[..header.PacketSize].CopyTo(mem.Memory[(Index)(i*size+Constants.HeaderSize)..]);
									ChatVoicePacket.SetDistance(mem.Memory.Span[(Index)(i*size+Constants.HeaderSize)..], Vector3.Distance((Vector3)to[i].Metadata["position"]!, pos));
								}
								List<EndPoint> clientEPs = to.Select(c => (EndPoint)c.ChatEP!).ToList();
								BroadcastUdp(serverSocket, mem, clientEPs, size, token);
								packetBuf.Dispose();
							}
						}
						break;
					
					default:
						Logger.Warn($"Chat socket received packet of invalid type {header.Type}");
						packetBuf.Dispose();
						continue;
				}
				
			}
		}
			catch (OperationCanceledException) {
				// ignore the exception, it's just for closing the server

				Logger.Info("Chat server closing");

				try {
					serverSocket.Shutdown(SocketShutdown.Both);
				}
				catch {
					// ignored
				}
				finally {
					serverSocket.Close();
				}

				Logger.Info("Chat server closed");
			}
    }

	private async Task SendToAsync(Socket socket, Memory<byte> buf, EndPoint ep, CancellationToken? ct) {
		int sent = await(ct == null ? socket.SendToAsync(buf[..Constants.HeaderSize], SocketFlags.None, ep)
			: socket.SendToAsync(buf[..Constants.HeaderSize], SocketFlags.None, ep, ct.Value));
		sent = await(ct == null ? socket.SendToAsync(buf[Constants.HeaderSize..], SocketFlags.None, ep)
			: socket.SendToAsync(buf[Constants.HeaderSize..], SocketFlags.None, ep, ct.Value));
	}

	private async Task SendToAsyncAndDispose(Socket socket, IMemoryOwner<byte> buf, EndPoint ep, CancellationToken? ct) {
		SendToAsync(socket, buf.Memory, ep, ct);
		buf.Dispose();
	}

	private async Task BroadcastUdp(Socket socket, IMemoryOwner<byte> packets, List<EndPoint> clients, short size, CancellationToken? ct) {
		await Task.WhenAll(clients.Select( (c, i) => SendToAsync(socket, packets.Memory[(Index)(i*size)..], c, ct) ) );
		packets.Dispose();
	}

    public static void FillPacket<T>(PacketHeader header, T packet, Memory<byte> memory) where T : struct, IPacket {
        Span<byte> data = memory.Span;
        
        header.Serialize(data[..Constants.HeaderSize]);
        packet.Serialize(data[Constants.HeaderSize..]);
    }

    // broadcast packets to all clients
    public delegate void PacketReplacer<in T>(Client from, Client to, T value); // replacer must send

    public void BroadcastReplace<T>(T packet, Client sender, PacketReplacer<T> packetReplacer) where T : struct, IPacket {
        foreach (Client client in Clients.Where(client => client.Connected && sender.Id != client.Id)) packetReplacer(sender, client, packet);
    }

    public async Task Broadcast<T>(T packet, Client sender) where T : struct, IPacket {
        IMemoryOwner<byte> memory = MemoryPool<byte>.Shared.RentZero(Constants.HeaderSize + packet.Size);
        PacketHeader header = new PacketHeader {
            Id = sender?.Id ?? Guid.Empty,
            Type = Constants.PacketMap[typeof(T)].Type,
            PacketSize = packet.Size
        };
        FillPacket(header, packet, memory.Memory);
        await Broadcast(memory, sender);
    }

    public Task Broadcast<T>(T packet) where T : struct, IPacket {
        return Task.WhenAll(Clients.Where(c => c.Connected).Select(async client => {
            IMemoryOwner<byte> memory = MemoryPool<byte>.Shared.RentZero(Constants.HeaderSize + packet.Size);
            PacketHeader header = new PacketHeader {
                Id = client.Id,
                Type = Constants.PacketMap[typeof(T)].Type,
                PacketSize = packet.Size
            };
            FillPacket(header, packet, memory.Memory);
            await client.Send(memory.Memory, client);
            memory.Dispose();
        }));
    }

    /// <summary>
    ///     Takes ownership of data and disposes once done.
    /// </summary>
    /// <param name="data">Memory owner to dispose once done</param>
    /// <param name="sender">Optional sender to not broadcast data to</param>
    public async Task Broadcast(IMemoryOwner<byte> data, Client? sender = null) {
        await Task.WhenAll(Clients.Where(c => c.Connected && c != sender).Select(client => client.Send(data.Memory, sender)));
        data.Dispose();
    }

    /// <summary>
    ///     Broadcasts memory whose memory shouldn't be disposed, should only be fired by server code.
    /// </summary>
    /// <param name="data">Memory to send to the clients</param>
    /// <param name="sender">Optional sender to not broadcast data to</param>
    public async void Broadcast(Memory<byte> data, Client? sender = null) {
        await Task.WhenAll(Clients.Where(c => c.Connected && c != sender).Select(client => client.Send(data, sender)));
    }

    public Client? FindExistingClient(Guid id) {
        return Clients.Find(client => client.Id == id);
    }


    private async void HandleSocket(Socket socket) {
        Client client = new Client(socket) {Server = this};
        IMemoryOwner<byte> memory = null!;
        await client.Send(new InitPacket {
            MaxPlayers = Settings.Instance.Server.MaxPlayers
        });
        bool first = true;
        try {
            while (true) {
                memory = memoryPool.Rent(Constants.HeaderSize);

                async Task<bool> Read(Memory<byte> readMem, int readSize, int readOffset) {
                    readSize += readOffset;
                    while (readOffset < readSize) {
                        int size = await socket.ReceiveAsync(readMem[readOffset..readSize], SocketFlags.None);
                        if (size == 0) {
                            // treat it as a disconnect and exit
                            Logger.Info($"Socket {socket.RemoteEndPoint} disconnected.");
                            if (socket.Connected) await socket.DisconnectAsync(false);
                            return false;
                        }

                        readOffset += size;
                    }

                    return true;
                }

                if (!await Read(memory.Memory[..Constants.HeaderSize], Constants.HeaderSize, 0))
                    break;
                PacketHeader header = GetHeader(memory.Memory.Span[..Constants.HeaderSize]);
                Range packetRange = Constants.HeaderSize..(Constants.HeaderSize + header.PacketSize);
                if (header.PacketSize > 0) {
                    IMemoryOwner<byte> memTemp = memory; // header to copy to new memory
                    memory = memoryPool.Rent(Constants.HeaderSize + header.PacketSize);
                    memTemp.Memory.Span[..Constants.HeaderSize].CopyTo(memory.Memory.Span[..Constants.HeaderSize]);
                    memTemp.Dispose();
                    if (!await Read(memory.Memory, header.PacketSize, Constants.HeaderSize))
                        break;
                }

                // connection initialization
                if (first) {
                    first = false;
                    if (header.Type != PacketType.Connect) throw new Exception($"First packet was not init, instead it was {header.Type}");

                    ConnectPacket connect = new ConnectPacket();
                    connect.Deserialize(memory.Memory.Span[packetRange]);
                    lock (Clients) {
                        client.Name = connect.ClientName;
                        if (Clients.Count(x => x.Connected) == Settings.Instance.Server.MaxPlayers) {
                            client.Logger.Error($"Turned away as server is at max clients");
                            memory.Dispose();
                            goto disconnect;
                        }

                        bool firstConn = false;
                        switch (connect.ConnectionType) {
                            case ConnectPacket.ConnectionTypes.FirstConnection: {
                                firstConn = true;
                                if (FindExistingClient(header.Id) is { } newClient) {
                                    if (newClient.Connected) {
                                        newClient.Logger.Info($"Disconnecting already connected client {newClient.Socket?.RemoteEndPoint} for {client.Socket?.RemoteEndPoint}");
                                        newClient.Dispose();
                                    }
                                    newClient.Socket = client.Socket;
                                    client = newClient;
                                }

                                break;
                            }
                            case ConnectPacket.ConnectionTypes.Reconnecting: {
                                client.Id = header.Id;
                                if (FindExistingClient(header.Id) is { } newClient) {
                                    if (newClient.Connected) {
                                        newClient.Logger.Info($"Disconnecting already connected client {newClient.Socket?.RemoteEndPoint} for {client.Socket?.RemoteEndPoint}");
                                        newClient.Dispose();
                                    }
                                    newClient.Socket = client.Socket;
                                    client = newClient;
                                } else {
                                    firstConn = true;
                                    connect.ConnectionType = ConnectPacket.ConnectionTypes.FirstConnection;
                                }

                                break;
                            }
                            default:
                                throw new Exception($"Invalid connection type {connect.ConnectionType}");
                        }

                        client.Connected = true;
                        if (firstConn) {
                            // do any cleanup required when it comes to new clients
                            List<Client> toDisconnect = Clients.FindAll(c => c.Id == header.Id && c.Connected && c.Socket != null);
                            Clients.RemoveAll(c => c.Id == header.Id);

                            client.Id = header.Id;
                            Clients.Add(client);

                            Parallel.ForEachAsync(toDisconnect, (c, token) => c.Socket!.DisconnectAsync(false, token));
                            // done disconnecting and removing stale clients with the same id

                            ClientJoined?.Invoke(client, connect);
                        }
                    }

                    List<Client> otherConnectedPlayers = Clients.FindAll(c => c.Id != header.Id && c.Connected && c.Socket != null);
                    await Parallel.ForEachAsync(otherConnectedPlayers, async (other, _) => {
                        IMemoryOwner<byte> tempBuffer = MemoryPool<byte>.Shared.RentZero(Constants.HeaderSize + (other.CurrentCostume.HasValue ? Math.Max(connect.Size, other.CurrentCostume.Value.Size) : connect.Size));
                        PacketHeader connectHeader = new PacketHeader {
                            Id = other.Id,
                            Type = PacketType.Connect,
                            PacketSize = connect.Size
                        };
                        connectHeader.Serialize(tempBuffer.Memory.Span[..Constants.HeaderSize]);
                        ConnectPacket connectPacket = new ConnectPacket {
                            ConnectionType = ConnectPacket.ConnectionTypes.FirstConnection, // doesn't matter what it is
                            MaxPlayers = Settings.Instance.Server.MaxPlayers,
                            ClientName = other.Name
                        };
                        connectPacket.Serialize(tempBuffer.Memory.Span[Constants.HeaderSize..]);
                        await client.Send(tempBuffer.Memory[..(Constants.HeaderSize + connect.Size)], null);
                        if (other.CurrentCostume.HasValue) {
                            connectHeader.Type = PacketType.Costume;
                            connectHeader.PacketSize = other.CurrentCostume.Value.Size;
                            connectHeader.Serialize(tempBuffer.Memory.Span[..Constants.HeaderSize]);
                            other.CurrentCostume.Value.Serialize(tempBuffer.Memory.Span[Constants.HeaderSize..(Constants.HeaderSize + connectHeader.PacketSize)]);
                            await client.Send(tempBuffer.Memory[..(Constants.HeaderSize + connectHeader.PacketSize)], null);
                        }

                        tempBuffer.Dispose();
                    });

                    Logger.Info($"Client {client.Name} ({client.Id}/{socket.RemoteEndPoint}) connected.");
                } else if (header.Id != client.Id && client.Id != Guid.Empty) {
                    throw new Exception($"Client {client.Name} sent packet with invalid client id {header.Id} instead of {client.Id}");
                }

                if (header.Type == PacketType.Costume) {
                    CostumePacket costumePacket = new CostumePacket {
                        BodyName = ""
                    };
                    costumePacket.Deserialize(memory.Memory.Span[Constants.HeaderSize..(Constants.HeaderSize + costumePacket.Size)]);
                    client.CurrentCostume = costumePacket;
                }

                try {
                    IPacket packet = (IPacket) Activator.CreateInstance(Constants.PacketIdMap[header.Type])!;
                    packet.Deserialize(memory.Memory.Span[Constants.HeaderSize..(Constants.HeaderSize + packet.Size)]);
                    if (PacketHandler?.Invoke(client, packet) is false) {
                        memory.Dispose();
                        continue;
                    }
                }
                catch (Exception e) {
                    client.Logger.Error($"Packet handler warning: {e}");
                }

                Broadcast(memory, client);
            }
        }
        catch (Exception e) {
            if (e is SocketException {SocketErrorCode: SocketError.ConnectionReset}) {
                client.Logger.Info($"Disconnected from the server: Connection reset");
            } else {
                client.Logger.Error($"Disconnecting due to exception: {e}");
                if (socket.Connected) Task.Run(() => socket.DisconnectAsync(false));
            }

            memory?.Dispose();
        }

        disconnect:
        Logger.Info($"Client {socket.RemoteEndPoint} ({client.Name}/{client.Id}) disconnected from the server");

        // Clients.Remove(client)
        client.Connected = false;
        try {
            client.Dispose();
        }
        catch { /*lol*/ }

        Task.Run(() => Broadcast(new DisconnectPacket(), client));
    }

    private static PacketHeader GetHeader(Span<byte> data) {
        //no need to error check, the client will disconnect when the packet is invalid :)
        PacketHeader header = new PacketHeader();
        header.Deserialize(data[..Constants.HeaderSize]);
        return header;
    }
}
