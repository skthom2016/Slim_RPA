namespace Tracto.Desktop.Session;

/// <summary>Describes the display configuration of the Windows desktop.</summary>
/// <param name="WidthPx">Horizontal resolution in physical pixels.</param>
/// <param name="HeightPx">Vertical resolution in physical pixels.</param>
/// <param name="DpiX">Horizontal dots per inch.</param>
/// <param name="DpiY">Vertical dots per inch.</param>
public sealed record DisplayProfile(int WidthPx, int HeightPx, int DpiX, int DpiY);
