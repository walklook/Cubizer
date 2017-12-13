﻿using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

using UnityEngine;

using Cubizer.Protocol;
using Cubizer.Protocol.Serialization;

namespace Cubizer.Client
{
	public sealed class ClientSession : IDisposable
	{
		private readonly int _port;
		private readonly string _hostname;
		private readonly IPacketRouter _packRouter;
		private readonly IPacketCompress _packetCompress;
		private readonly ClientDelegates _events = new ClientDelegates();
		private readonly CompressedPacket _compressedPacket = new CompressedPacket();

		private int _sendTimeout = 0;
		private int _receiveTimeout = 0;

		private Task _tcpTask;
		private TcpClient _tcpClient;

		public int sendTimeout
		{
			set
			{
				if (_sendTimeout != value)
				{
					if (_tcpClient != null)
						_tcpClient.SendTimeout = value;

					_sendTimeout = value;
				}
			}
			get
			{
				return _sendTimeout;
			}
		}

		public int receiveTimeout
		{
			set
			{
				if (_receiveTimeout != value)
				{
					if (_tcpClient != null)
						_tcpClient.ReceiveTimeout = value;

					_receiveTimeout = value;
				}
			}
			get
			{
				return _receiveTimeout;
			}
		}

		public bool connected
		{
			get
			{
				return _tcpClient != null ? _tcpClient.Connected : false;
			}
		}

		public ClientDelegates events
		{
			get
			{
				return _events;
			}
		}

		public ClientSession(string hostname, int port, IPacketRouter protocal, IPacketCompress packetCompress = null)
		{
			Debug.Assert(protocal != null);

			_port = port;
			_hostname = hostname;
			_packRouter = protocal;
			_packetCompress = packetCompress ?? new PacketCompress();
		}

		~ClientSession()
		{
			this.Close();
		}

		public bool Connect()
		{
			Debug.Assert(_tcpClient == null);

			try
			{
				_tcpClient = new TcpClient();
				_tcpClient.SendTimeout = _sendTimeout;
				_tcpClient.ReceiveTimeout = _receiveTimeout;
				_tcpClient.Connect(_hostname, _port);

				return _tcpClient.Connected;
			}
			catch (Exception)
			{
				_tcpClient.Close();
				_tcpClient = null;
				return false;
			}
		}

		public Task Start(CancellationToken cancellationToken)
		{
			if (!_tcpClient.Connected)
				throw new InvalidOperationException("Please connect the server before Start()");

			_tcpTask = Task.Run(async () =>
			{
				using (var stream = _tcpClient.GetStream())
				{
					try
					{
						if (_events.onStartClientListener != null)
							_events.onStartClientListener.Invoke();

						while (!cancellationToken.IsCancellationRequested)
							await DispatchIncomingPacket(stream);
					}
					finally
					{
						if (_events.onStopClientListener != null)
							_events.onStopClientListener.Invoke();
					}
				}
			});

			return _tcpTask;
		}

		public void Close()
		{
			try
			{
				if (_tcpTask != null)
					_tcpTask.Wait();
			}
			catch (Exception e)
			{
				UnityEngine.Debug.LogException(e);
			}
			finally
			{
				_tcpTask = null;

				if (_tcpClient != null)
				{
					_tcpClient.Close();
					_tcpClient = null;
				}
			}
		}

		public async Task SendUncompressedPacket(UncompressedPacket packet)
		{
			if (packet == null)
				_tcpClient.Client.Shutdown(SocketShutdown.Send);
			else
			{
				var newPacket = _packetCompress.Compress(packet);
				await newPacket.SerializeAsync(_tcpClient.GetStream());
			}
		}

		public async Task SendPacket(IPacketSerializable packet)
		{
			if (packet == null)
				await SendUncompressedPacket(null);
			else
			{
				using (var stream = new MemoryStream())
				{
					using (var bw = new NetworkWrite(stream))
						packet.Serialize(bw);

					await SendUncompressedPacket(new UncompressedPacket(packet.packId, new ArraySegment<byte>(stream.ToArray())));
				}
			}
		}

		public void Dispose()
		{
			this.Close();
		}

		private async Task DispatchIncomingPacket(Stream stream)
		{
			int count = await _compressedPacket.DeserializeAsync(stream);
			if (count > 0)
				await DispatchIncomingPacket(_packetCompress.Decompress(_compressedPacket));
			else
				throw new EndOfStreamException();
		}

		private async Task DispatchIncomingPacket(UncompressedPacket packet)
		{
			await _packRouter.DispatchIncomingPacket(packet);
		}
	}
}