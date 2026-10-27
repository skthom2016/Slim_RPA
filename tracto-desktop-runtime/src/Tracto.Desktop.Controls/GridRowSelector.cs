namespace Tracto.Desktop.Controls;

/// <summary>
/// Identifies a grid row by stable content-based identity or by row index.
/// Content-based selection (MatchCellValue / ColumnName) is preferred because it
/// survives sort and filter operations; ordinal RowIndex is a last-resort fallback.
/// </summary>
/// <param name="MatchCellValue">
/// A cell value in the key column used to uniquely identify the target row.
/// When supplied, the engine locates the row whose cell in <paramref name="ColumnName"/>
/// equals this value regardless of current visual order.
/// </param>
/// <param name="ColumnName">
/// The column header name in which <paramref name="MatchCellValue"/> is searched.
/// Ignored when <paramref name="MatchCellValue"/> is null.
/// </param>
/// <param name="RowIndex">
/// Zero-based ordinal row index used when <paramref name="MatchCellValue"/> is null.
/// This is position-dependent and may break after sort; prefer content-based targeting.
/// </param>
public sealed record GridRowSelector(
    string? MatchCellValue,
    string? ColumnName,
    int?    RowIndex);
