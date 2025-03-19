namespace TURNServer;

public enum NATTypeEnum
{
	/// <summary>
	/// 未知
	/// </summary>
	Unknown,
	/// <summary>
	/// 全锥形
	/// </summary>
	FullCone,
	/// <summary>
	/// 限制锥形
	/// </summary>
	RestrictedCone,
	/// <summary>
	/// 端口限制锥形
	/// </summary>
	PortRestrictedCone,
	/// <summary>
	/// 对称形
	/// </summary>
	Symmetric
}