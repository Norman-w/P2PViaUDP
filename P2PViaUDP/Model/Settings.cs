namespace P2PViaUDP.Model;

public class Settings
{
	/// <summary>
	/// STUN (Session Traversal Utilities for NAT [会话遍历NAT的实用工具])服务器IP
	/// </summary>
	public string STUNServerIP { get; set; } = "127.0.0.1";
	/// <summary>
	///	TURN (Traversal Using Relays around NAT [绕过NAT的中继遍历])服务器IP
	/// </summary>
	public string TURNServerIP { get; set; } = "127.0.0.1";
	/// <summary>
	/// STUN服务器端口
	/// </summary>
	public ushort STUNServerPort { get; set; } = 3478;
	/// <summary>
	/// TURN服务器端口
	/// </summary>
	public ushort TURNServerPort { get; set; } = 3749;
}