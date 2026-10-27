using System.Windows.Automation;
using Tracto.Desktop.Element;

namespace Tracto.Desktop.Controls;

/// <summary>
/// Production implementation of <see cref="IGridInteractionEngine"/>.
/// Uses <see cref="IUiaPatternProvider"/> to interact with Windows DataGrid and
/// DataGridView controls via GridPattern, TablePattern and VirtualizedItemPattern.
/// </summary>
public sealed class GridInteractionEngine : IGridInteractionEngine
{
    private readonly IUiaPatternProvider _patterns;

    public GridInteractionEngine(IUiaPatternProvider patterns)
    {
        _patterns = patterns ?? throw new ArgumentNullException(nameof(patterns));
    }

    // ── SelectRowAsync ────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public Task SelectRowAsync(
        ElementRef        gridEl,
        GridRowSelector   selector,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(gridEl);
        ArgumentNullException.ThrowIfNull(selector);

        int rowIndex = ResolveRowIndex(gridEl, selector);
        _patterns.SelectGridRow(gridEl, rowIndex);

        return Task.CompletedTask;
    }

    // ── EditCellAsync ─────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public Task EditCellAsync(
        ElementRef        gridEl,
        int               row,
        int               column,
        string            newValue,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(gridEl);
        ArgumentNullException.ThrowIfNull(newValue);

        // Read-only check — must happen before entering edit mode.
        if (_patterns.IsCellReadOnly(gridEl, row, column))
            throw new DesktopActionError("ENVIRONMENT",
                "desktop.gridCellReadOnly");

        _patterns.SetCellValue(gridEl, row, column, newValue);

        return Task.CompletedTask;
    }

    // ── ScrollToRowAsync ──────────────────────────────────────────────────────

    /// <inheritdoc/>
    public Task ScrollToRowAsync(
        ElementRef        gridEl,
        GridRowSelector   selector,
        TimeSpan          timeout,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(gridEl);
        ArgumentNullException.ThrowIfNull(selector);

        int rowIndex = ResolveRowIndex(gridEl, selector);

        // Attempt VirtualizedItemPattern.Realize() within the bounded timeout.
        bool realized = _patterns.TryRealizeRow(gridEl, rowIndex, timeout);

        if (!realized)
        {
            // Determine the more specific failure code.
            // If the grid supports the pattern but it timed out → TIMEOUT.
            // If the grid does not support programmatic scrolling → ENVIRONMENT.
            // We use TIMEOUT as the primary signal; the caller may inspect detail.
            throw new DesktopActionError("TIMEOUT",
                "desktop.virtualizedRowRealizationTimeout");
        }

        return Task.CompletedTask;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Resolves the zero-based row index for <paramref name="selector"/>.
    /// Prefers content-based matching over ordinal index.
    /// </summary>
    private int ResolveRowIndex(ElementRef gridEl, GridRowSelector selector)
    {
        if (selector.MatchCellValue is not null)
        {
            IReadOnlyList<string> columns = _patterns.GetGridColumnNames(gridEl);
            int colIndex = 0;

            if (selector.ColumnName is not null)
            {
                colIndex = -1;
                for (int c = 0; c < columns.Count; c++)
                {
                    if (string.Equals(columns[c], selector.ColumnName,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        colIndex = c;
                        break;
                    }
                }

                if (colIndex < 0)
                    throw new DesktopActionError("TARGET_NOT_FOUND",
                        $"desktop.gridColumnNotFound: '{selector.ColumnName}'");
            }

            int rowCount = _patterns.GetGridRowCount(gridEl);
            for (int r = 0; r < rowCount; r++)
            {
                string? cellValue = _patterns.GetGridCellValue(gridEl, r, colIndex);
                if (string.Equals(cellValue, selector.MatchCellValue,
                        StringComparison.OrdinalIgnoreCase))
                    return r;
            }

            throw new DesktopActionError("TARGET_NOT_FOUND",
                $"desktop.gridRowNotFound: cell '{selector.MatchCellValue}' in column index {colIndex}");
        }

        if (selector.RowIndex.HasValue)
            return selector.RowIndex.Value;

        throw new ArgumentException(
            "GridRowSelector must specify MatchCellValue or RowIndex.",
            nameof(selector));
    }
}
