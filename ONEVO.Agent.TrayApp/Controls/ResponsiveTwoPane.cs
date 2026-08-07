namespace ONEVO.Agent.TrayApp.Controls;

/// <summary>
/// Switches a named content Grid between side-by-side (wide) and stacked (narrow) layouts.
/// Attach by calling <see cref="Attach"/> from page code-behind with the pane Grid.
/// </summary>
public static class ResponsiveTwoPane
{
    public const double WideBreakpoint = 860;

    public static void Attach(VisualElement host, Grid paneGrid, View? leftPane = null, View? rightPane = null)
    {
        // Capture the design-time wide column setup (if any) once.
        var wideColumns = paneGrid.ColumnDefinitions
            .Select(c => new ColumnDefinition(c.Width))
            .ToList();
        if (wideColumns.Count < 2)
        {
            wideColumns =
            [
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star)
            ];
        }

        var wideColumnSpacing = paneGrid.ColumnSpacing > 0 ? paneGrid.ColumnSpacing : 24;

        void Apply(double width)
        {
            if (width <= 0) return;

            if (width < WideBreakpoint)
            {
                // Stack: left on top, right below
                paneGrid.ColumnDefinitions.Clear();
                paneGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
                paneGrid.RowDefinitions.Clear();
                paneGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                paneGrid.RowDefinitions.Add(new RowDefinition(GridLength.Star));
                paneGrid.ColumnSpacing = 0;
                paneGrid.RowSpacing = 16;

                if (leftPane is not null)
                {
                    Grid.SetColumn(leftPane, 0);
                    Grid.SetRow(leftPane, 0);
                    leftPane.MaximumHeightRequest = 180;
                }
                if (rightPane is not null)
                {
                    Grid.SetColumn(rightPane, 0);
                    Grid.SetRow(rightPane, 1);
                }
            }
            else
            {
                // Side-by-side — restore design-time columns
                paneGrid.RowDefinitions.Clear();
                paneGrid.RowDefinitions.Add(new RowDefinition(GridLength.Star));
                paneGrid.ColumnDefinitions.Clear();
                foreach (var col in wideColumns)
                    paneGrid.ColumnDefinitions.Add(new ColumnDefinition(col.Width));
                paneGrid.ColumnSpacing = wideColumnSpacing;
                paneGrid.RowSpacing = 0;

                if (leftPane is not null)
                {
                    Grid.SetColumn(leftPane, 0);
                    Grid.SetRow(leftPane, 0);
                    leftPane.MaximumHeightRequest = double.PositiveInfinity;
                }
                if (rightPane is not null)
                {
                    Grid.SetColumn(rightPane, 1);
                    Grid.SetRow(rightPane, 0);
                }
            }
        }

        host.SizeChanged += (_, _) => Apply(host.Width);
        host.Loaded += (_, _) => Apply(host.Width);
    }
}
