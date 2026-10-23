using System.Windows;
using System.Windows.Controls;

namespace StorageOptimizer.App.Infrastructure;

/// <summary>
/// Lays children out left to right, each as wide as its share of <see cref="Total"/>
/// (set with the attached <c>Weight</c> property), with a small gap between them.
/// Used for the stacked drive usage bar.
/// </summary>
public sealed class ProportionalPanel : Panel
{
    public static readonly DependencyProperty WeightProperty = DependencyProperty.RegisterAttached(
        "Weight", typeof(double), typeof(ProportionalPanel),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsParentArrange));

    public static readonly DependencyProperty TotalProperty = DependencyProperty.Register(
        nameof(Total), typeof(double), typeof(ProportionalPanel),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsArrange));

    public static double GetWeight(DependencyObject element) => (double)element.GetValue(WeightProperty);
    public static void SetWeight(DependencyObject element, double value) => element.SetValue(WeightProperty, value);

    /// <summary>The weight that fills the whole width; defaults to the sum of the weights.</summary>
    public double Total
    {
        get => (double)GetValue(TotalProperty);
        set => SetValue(TotalProperty, value);
    }

    public double Gap { get; set; } = 2;

    protected override Size MeasureOverride(Size availableSize)
    {
        var height = 0.0;
        foreach (UIElement child in InternalChildren)
        {
            child.Measure(new Size(double.PositiveInfinity, availableSize.Height));
            height = Math.Max(height, child.DesiredSize.Height);
        }
        var width = double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width;
        return new Size(width, double.IsInfinity(availableSize.Height) ? height : availableSize.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var children = InternalChildren.Cast<UIElement>().ToList();
        var weights = children.Select(child => Math.Max(0, GetWeight(child))).ToList();
        var total = Total > 0 ? Total : weights.Sum();
        var visible = weights.Count(weight => weight > 0);
        var available = Math.Max(0, finalSize.Width - Gap * Math.Max(0, visible - 1));

        var x = 0.0;
        for (var index = 0; index < children.Count; index++)
        {
            var width = total > 0 && weights[index] > 0 ? Math.Max(2, available * weights[index] / total) : 0;
            width = Math.Min(width, Math.Max(0, finalSize.Width - x));
            children[index].Arrange(new Rect(x, 0, width, finalSize.Height));
            if (width > 0)
            {
                x += width + Gap;
            }
        }
        return finalSize;
    }
}
