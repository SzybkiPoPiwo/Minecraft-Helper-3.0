using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;


namespace MinecraftHelper
{
    /// <summary>Proste pikselowe ikony przedmiotów (16×16, do czterech warstw) używane w kafelkach Auto EQ.</summary>
    internal static class ItemPixelIcons
    {
        private static (string, string)[] Ingot(string fill, string light, string dark, string shine) => new[]
        {
            ("M4 5h8l3 6H1z", fill), ("M4 5h8l1 2H3z", light), ("M1 11h14v2H1z", dark), ("M5 6h3v1H5z", shine)
        };

        private static (string, string)[] Block(string fill, string light, string dark, string shine) => new[]
        {
            ("M2 2h12v12H2z", fill), ("M2 2h12v2H4v10H2z", light), ("M14 14H4v-2h8V4h2z", dark), ("M6 6h2v2H6z", shine)
        };

        private static readonly Dictionary<string, (string Data, string Color)[]> Icons = new()
        {
            ["diamond"] = new[] { ("M2 6l3-4h6l3 4-6 8z", "#2BC4BC"), ("M5 2h6l3 4H2z", "#A8F5F0"), ("M8 14 5 6h6z", "#5FE3DA"), ("M5 3h2v1H5z", "#FFFFFF") },
            ["gold_ingot"] = Ingot("#E9B934", "#FFE27A", "#B88A12", "#FFF2B0"),
            ["gold_block"] = Block("#E9B934", "#FFE27A", "#B88A12", "#FFF2B0"),
            ["iron_ingot"] = Ingot("#CFCFCF", "#F2F2F2", "#8A8A8A", "#FFFFFF"),
            ["iron_block"] = Block("#CFCFCF", "#F2F2F2", "#8A8A8A", "#FFFFFF"),
            ["emerald"] = new[] { ("M8 1l4 4v6l-4 4-4-4V5z", "#22B864"), ("M8 1l4 4H4z", "#7DF2A8"), ("M8 15l-4-4h8z", "#12803F"), ("M7 4h2v1H7z", "#D6FFE6") },
            ["emerald_block"] = Block("#22B864", "#7DF2A8", "#12803F", "#D6FFE6"),
            ["obsidian"] = new[] { ("M2 2h12v12H2z", "#2A1A3D"), ("M3 3h4v3H3zM9 8h4v3H9z", "#4B3270"), ("M8 3h5v2H8zM3 9h4v4H3z", "#160C22"), ("M4 4h1v1H4zM10 9h1v1h-1z", "#8A6BC2") },
            ["apple"] = new[] { ("M3 6h3V5h4v1h3v6h-1v2H4v-2H3z", "#D93636"), ("M4 7h2v2H4z", "#FF8A80"), ("M8 2h1v3H8z", "#6B4630"), ("M9 2h3v2H9z", "#43A047") },
            ["sand"] = new[] { ("M2 2h12v12H2z", "#E3D29B"), ("M2 2h12v2H4v10H2z", "#F3E8C2"), ("M14 14H4v-2h8V4h2z", "#C9B576"), ("M6 7h1v1H6zM10 9h1v1h-1zM8 11h1v1H8z", "#B8A262") },
            ["gunpowder"] = new[] { ("M2 12h12v2H2z", "#4F4F4F"), ("M3 12l2-3 2-1 2 1 2-2 2 5z", "#767676"), ("M6 9h1v1H6zM10 8h1v1h-1z", "#A9A9A9"), ("M8 11h1v1H8z", "#3A3A3A") },
            ["coal"] = new[] { ("M4 4h7l2 3-1 6H5L3 9z", "#2B2B2B"), ("M5 5h3v1H5zM9 9h2v1H9z", "#5C5C5C"), ("M4 11h8v2H5z", "#161616"), ("M6 7h1v1H6z", "#7A7A7A") },
            ["quartz"] = new[] { ("M5 3h6l2 4-5 7-5-7z", "#ECE6DC"), ("M5 3h6l2 4H3z", "#FFFFFF"), ("M8 14l-2-7h4z", "#CFC5B5"), ("M6 4h1v1H6z", "#FFFFFF") },
            ["book"] = new[] { ("M3 2h10v12H3z", "#8E4A26"), ("M4 11h8v2H4z", "#F3E9D2"), ("M3 2h2v12H3z", "#5E2F16"), ("M7 5h4v2H7z", "#D9A441") },
            ["ender_pearl"] = new[] { ("M8 2a6 6 0 1 1 0 12A6 6 0 0 1 8 2z", "#12705F"), ("M8 5a3 3 0 1 1 0 6 3 3 0 0 1 0-6z", "#3FD1B0"), ("M8 7a1 1 0 1 1 0 2 1 1 0 0 1 0-2z", "#0B3D34"), ("M5 4h2v1H5z", "#B6F7E6") },
            ["redstone"] = new[] { ("M2 12h12v1H2z", "#7A1010"), ("M3 12l2-3h2l1-2h2l2 3 1 2z", "#C62828"), ("M7 7h2v1H7zM4 10h1v1H4z", "#FF6B6B"), ("M10 10h1v1h-1z", "#8E1515") }
        };

        public static FrameworkElement Create(string itemId, double size)
        {
            var canvas = new Grid { Width = 16, Height = 16 };
            if (Icons.TryGetValue(itemId, out (string Data, string Color)[]? layers))
            {
                foreach ((string data, string color) in layers)
                {
                    canvas.Children.Add(new System.Windows.Shapes.Path
                    {
                        Data = Geometry.Parse(data),
                        Fill = (Brush)new BrushConverter().ConvertFromString(color)!,
                        SnapsToDevicePixels = true
                    });
                }
            }

            RenderOptions.SetEdgeMode(canvas, EdgeMode.Aliased);
            return new Viewbox { Width = size, Height = size, Child = canvas, Stretch = Stretch.Uniform };
        }
    }
}
