using System.Net;

namespace P2PViaUDP.Model.STUN;

public class StunNATTypeCheckingResponse
{
	public StunNATTypeCheckingResponse(
		Guid requestId, 
		bool isFromMainSTUNServer, 
		bool isFromSlaveSTUNServer, 
		IPEndPoint stunServerEndPoint, 
		IPEndPoint detectedClientNATEndPoint,
		DateTime sendTime
	)
	{
		RequestId = requestId;
		IsFromMainSTUNServer = isFromMainSTUNServer;
		IsFromSlaveSTUNServer = isFromSlaveSTUNServer;
		StunServerEndPoint = stunServerEndPoint;
		DetectedClientNATEndPoint = detectedClientNATEndPoint;
		SendTime = sendTime;
	}
	private static MessageType MessageType { get; } = MessageType.StunNATTypeCheckingResponse;
	//TODO TBD
	private static ushort DefaultContentLength => 0;
	
	/// <summary>
	/// 指示该响应是对应的哪个请求,请求ID是客户端分配的
	/// </summary>
	public Guid RequestId { get; init; }
	/// <summary>
	/// 该响应是否由主STUN服务器返回,与IsSlaveMainSTUNServer始终互斥
	/// </summary>
	public bool IsFromMainSTUNServer { get; init; }
	/// <summary>
	/// 该响应是否由从STUN服务器返回,与IsMainSTUNServer始终互斥
	/// </summary>
	public bool IsFromSlaveSTUNServer { get; init; }
	/// <summary>
	/// 指示该返回是由哪个STUN服务器的端点发回来的
	/// </summary>
	public IPEndPoint StunServerEndPoint { get; init; }
	/// <summary>
	/// STUN服务器监测到的客户端的出网时的外网端点
	/// </summary>
	public IPEndPoint DetectedClientNATEndPoint { get; init; }
	/// <summary>
	/// 该响应的发送时间
	/// </summary>
	public DateTime SendTime { get; init; }

	public byte[] ToBytes()
	{
		var bytesList = new List<byte>();
		bytesList.AddRange(BitConverter.GetBytes((int)MessageType));
		bytesList.AddRange(RequestId.ToByteArray());
		bytesList.Add((byte)(IsFromMainSTUNServer ? 1 : 0));
		bytesList.Add((byte)(IsFromSlaveSTUNServer ? 1 : 0));
		bytesList.AddRange(StunServerEndPoint.Address.GetAddressBytes());
		bytesList.AddRange(BitConverter.GetBytes(StunServerEndPoint.Port));
		bytesList.AddRange(DetectedClientNATEndPoint.Address.GetAddressBytes());
		bytesList.AddRange(BitConverter.GetBytes(DetectedClientNATEndPoint.Port));
		bytesList.AddRange(BitConverter.GetBytes(SendTime.Ticks));
		var bytes = bytesList.ToArray();
		return bytes;
	}
	public static StunNATTypeCheckingResponse FromBytes(byte[] bytes)
	{
		if (bytes.Length < DefaultContentLength)
		{
			throw new ArgumentException($"解析StunNATTypeCheckingResponse字节数组长度不正确,应为{DefaultContentLength},实际为{bytes.Length}");
		}
		if ((MessageType)BitConverter.ToInt32(bytes, 0) != MessageType)
		{
			throw new ArgumentException("消息类型不正确");
		}
		var requestId = new Guid(bytes.Skip(4).Take(16).ToArray());
		var isFromMainSTUNServer = bytes[20] == 1;
		var isFromSlaveSTUNServer = bytes[21] == 1;
		var stunServerEndPoint = new IPEndPoint(new IPAddress(bytes.Skip(22).Take(4).ToArray()), BitConverter.ToInt32(bytes, 26));
		var detectedClientNATEndPoint = new IPEndPoint(new IPAddress(bytes.Skip(30).Take(4).ToArray()), BitConverter.ToInt32(bytes, 34));
		var sendTime = new DateTime(BitConverter.ToInt64(bytes, 38));
		var result = new StunNATTypeCheckingResponse(
			requestId,
			isFromMainSTUNServer,
			isFromSlaveSTUNServer,
			stunServerEndPoint,
			detectedClientNATEndPoint,
			sendTime
		);
		return result;
	}
}