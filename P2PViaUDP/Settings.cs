namespace P2PViaUDP;

public class Settings
{
	/// <summary>
	/// STUN (Session Traversal Utilities for NAT [会话遍历NAT的实用工具])服务器IP
	/// </summary>
	public string STUNServerIP { get; set; } = "norman.wang";
	/// <summary>
	///	TURN (Traversal Using Relays around NAT [绕过NAT的中继遍历])服务器IP
	/// </summary>
	public string TURNServerIP { get; set; } = "norman.wang";
	/// <summary>
	/// STUN服务器端口
	/// </summary>
	public ushort STUNServerPort { get; set; } = 3478;
	/// <summary>
	/// 额外的STUN服务器端口,加上之前的一共20个,方便测试客户端的公网端口的变化规律
	/// </summary>
	public List<ushort> STUNServerAdditionalPorts { get; set; } = new()
	{
		3479, 
		3480, 
		3481, 
		// 3482, 
		// 3483, 
		// 3484, 
		// 3485, 
		// 3486, 
		// 3487, 
		// 3488, 
		// 3489, 
		// 3490, 
		// 3491, 
		// 3492, 
		// 3493, 
		// 3494, 
		// 3495, 
		// 3496, 
		// 3497, 
		// 3498
	};
	/// <summary>
	/// TURN服务器端口
	/// </summary>
	public ushort TURNServerPort { get; set; } = 3749;
}