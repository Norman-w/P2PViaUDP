using System.Net;

namespace P2PViaUDP.Model.Client;

/// <summary>
/// 客户端到客户端的P2P打洞消息
/// </summary>
public class Client2ClientP2PHolePunchingMessage
{
	/// <summary>
	/// 谁发起的
	/// </summary>
	public IPEndPoint SourceEndPoint { get; set; }
	/// <summary>
	/// 谁接收
	/// </summary>
	public IPEndPoint DestinationEndPoint { get; set; }
	/// <summary>
	/// 源客户端ID
	/// </summary>
	public Guid SourceClientId { get; set; }
	/// <summary>
	/// 目标客户端ID
	/// </summary>
	public Guid DestinationClientId { get; set; }
	/// <summary>
	/// 组ID
	/// </summary>
	public Guid GroupId { get; set; }
	/// <summary>
	/// 发送时间
	/// </summary>
	public DateTime SendTime { get; set; } = DateTime.Now;
	public byte[] ToBytes()
	{
		var bytesList = new List<byte>();
		bytesList.AddRange(SourceEndPoint.Address.GetAddressBytes());
		bytesList.AddRange(BitConverter.GetBytes(SourceEndPoint.Port));
		bytesList.AddRange(DestinationEndPoint.Address.GetAddressBytes());
		bytesList.AddRange(BitConverter.GetBytes(DestinationEndPoint.Port));
		bytesList.AddRange(SourceClientId.ToByteArray());
		bytesList.AddRange(DestinationClientId.ToByteArray());
		bytesList.AddRange(GroupId.ToByteArray());
		bytesList.AddRange(BitConverter.GetBytes(SendTime.Ticks));
		return bytesList.ToArray();
	}
	public static Client2ClientP2PHolePunchingMessage FromBytes(byte[] bytes)
	{
		var sourceEndPoint = new IPEndPoint(new IPAddress(bytes.Take(4).ToArray()), BitConverter.ToUInt16(bytes.Skip(4).Take(2).ToArray()));
		var destinationEndPoint = new IPEndPoint(new IPAddress(bytes.Skip(6).Take(4).ToArray()), BitConverter.ToUInt16(bytes.Skip(10).Take(2).ToArray()));
		var sourceClientId = new Guid(bytes.Skip(12).Take(16).ToArray());
		var destinationClientId = new Guid(bytes.Skip(28).Take(16).ToArray());
		var groupId = new Guid(bytes.Skip(44).Take(16).ToArray());
		var sendTime = new DateTime(BitConverter.ToInt64(bytes.Skip(60).Take(8).ToArray()));
		return new Client2ClientP2PHolePunchingMessage
		{
			SourceEndPoint = sourceEndPoint,
			DestinationEndPoint = destinationEndPoint,
			SourceClientId = sourceClientId,
			DestinationClientId = destinationClientId,
			GroupId = groupId,
			SendTime = sendTime
		};
	}
}