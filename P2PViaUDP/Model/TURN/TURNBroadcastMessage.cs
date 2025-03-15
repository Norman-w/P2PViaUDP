using System.Net;

namespace P2PViaUDP.Model.TURN;

public class TURNBroadcastMessage
{
	/// <summary>
	/// 客户端的Guid
	/// </summary>
	public Guid Guid { get; set; }
	/// <summary>
	/// 客户端的公网信息
	/// </summary>
	public IPEndPoint EndPoint { get; set; }
	/// <summary>
	/// 要加入的组Guid
	/// </summary>
	public Guid GroupGuid { get; set; }

	public byte[] ToBytes()
	{
		var guidBytes = Guid.ToByteArray();
		var endPointBytes = new byte[6];
		Array.Copy(EndPoint.Address.GetAddressBytes(), 0, endPointBytes, 0, 4);
		Array.Copy(BitConverter.GetBytes((ushort)EndPoint.Port), 0, endPointBytes, 4, 2);
		var groupGuidBytes = GroupGuid.ToByteArray();
		var result = new byte[38];
		Array.Copy(guidBytes, 0, result, 0, 16);
		Array.Copy(endPointBytes, 0, result, 16, 6);
		Array.Copy(groupGuidBytes, 0, result, 22, 16);
		return result;
	}
	public static TURNBroadcastMessage FromBytes(byte[] receivedBytes)
	{
		if (receivedBytes.Length != 38)
		{
			throw new ArgumentException("接收到的字节数组长度不正确");
		}
		var guidBytes = new byte[16];
		var endPointBytes = new byte[6];
		var groupGuidBytes = new byte[16];
		Array.Copy(receivedBytes, 0, guidBytes, 0, 16);
		Array.Copy(receivedBytes, 16, endPointBytes, 0, 6);
		Array.Copy(receivedBytes, 22, groupGuidBytes, 0, 16);
		return new TURNBroadcastMessage
		{
			Guid = new Guid(guidBytes),
			EndPoint = new IPEndPoint(new IPAddress(endPointBytes.Take(4).ToArray()), BitConverter.ToUInt16(endPointBytes.Skip(4).ToArray())),
			GroupGuid = new Guid(groupGuidBytes)
		};
	}
}