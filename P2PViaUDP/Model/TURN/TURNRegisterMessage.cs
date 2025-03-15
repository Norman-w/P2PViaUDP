using System.Net;

namespace P2PViaUDP.Model.TURN;

public class TURNRegisterMessage
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

	public static TURNRegisterMessage FromBytes(byte[] receivedBytes)
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
		return new TURNRegisterMessage
		{
			Guid = new Guid(guidBytes),
			EndPoint = new IPEndPoint(new IPAddress(endPointBytes.Take(4).ToArray()), BitConverter.ToUInt16(endPointBytes.Skip(4).ToArray())),
			GroupGuid = new Guid(groupGuidBytes)
		};
	}

	public byte[] ToBytes()
	{
		var bytesList = new List<byte>();
		bytesList.AddRange(Guid.ToByteArray());
		bytesList.AddRange(EndPoint.Address.GetAddressBytes());
		bytesList.AddRange(BitConverter.GetBytes((ushort)EndPoint.Port));
		bytesList.AddRange(GroupGuid.ToByteArray());
		return bytesList.ToArray();
	}
}