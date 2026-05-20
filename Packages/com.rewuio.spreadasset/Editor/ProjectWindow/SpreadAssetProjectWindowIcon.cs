using System;
using UnityEditor;
using UnityEngine;

namespace SpreadAsset.Editor
{
    [InitializeOnLoad]
    internal static class SpreadAssetProjectWindowIcon
    {
        private const int IconSize = 64;
        private static Texture2D _icon;

        static SpreadAssetProjectWindowIcon()
        {
            EditorApplication.projectWindowItemOnGUI += OnProjectWindowItemGUI;
        }

        private static void OnProjectWindowItemGUI(string guid, Rect selectionRect)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(guid);
            if (!SpreadAssetDocumentIO.IsDocumentPath(assetPath))
            {
                return;
            }

            Rect iconRect = GetIconRect(selectionRect);
            GUI.DrawTexture(iconRect, Icon, ScaleMode.ScaleToFit, true);
        }

        private static Rect GetIconRect(Rect selectionRect)
        {
            if (selectionRect.height > 22f)
            {
                float size = Mathf.Min(32f, selectionRect.width * 0.55f);
                return new Rect(
                    selectionRect.x + (selectionRect.width - size) * 0.5f,
                    selectionRect.y + 2f,
                    size,
                    size);
            }

            return new Rect(selectionRect.x, selectionRect.y + 1f, 16f, 16f);
        }

        private static Texture2D Icon
        {
            get
            {
                if (_icon == null)
                {
                    _icon = CreateIcon();
                    _icon.hideFlags = HideFlags.HideAndDontSave;
                }

                return _icon;
            }
        }

        private static Texture2D CreateIcon()
        {
            Texture2D texture = new Texture2D(IconSize, IconSize, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                name = "SpreadAsset Document Icon"
            };

            Color transparent = new Color(0f, 0f, 0f, 0f);
            Color shadow = new Color(0f, 0f, 0f, 0.22f);
            Color page = new Color(0.12f, 0.36f, 0.42f, 1f);
            Color pageLight = new Color(0.18f, 0.56f, 0.62f, 1f);
            Color fold = new Color(0.45f, 0.88f, 0.86f, 1f);
            Color grid = new Color(0.82f, 1f, 0.96f, 1f);
            Color accent = new Color(0.99f, 0.72f, 0.28f, 1f);

            Color[] pixels = new Color[IconSize * IconSize];
            Array.Fill(pixels, transparent);
            texture.SetPixels(pixels);

            FillRect(texture, 15, 11, 37, 45, shadow);
            FillRect(texture, 12, 8, 36, 45, page);
            FillRect(texture, 14, 10, 32, 41, pageLight);

            FillTriangle(texture, 35, 8, 47, 8, 47, 20, fold);
            FillTriangle(texture, 35, 9, 47, 20, 35, 20, page);

            FillRect(texture, 18, 24, 24, 2, grid);
            FillRect(texture, 18, 32, 24, 2, grid);
            FillRect(texture, 18, 40, 24, 2, grid);
            FillRect(texture, 25, 21, 2, 25, grid);
            FillRect(texture, 34, 21, 2, 25, grid);

            DrawLine(texture, 18, 24, 42, 24, grid);
            DrawLine(texture, 18, 32, 42, 32, grid);
            DrawLine(texture, 18, 40, 42, 40, grid);
            DrawLine(texture, 25, 21, 25, 46, grid);
            DrawLine(texture, 34, 21, 34, 46, grid);

            FillRect(texture, 18, 21, 24, 4, accent);
            FillRect(texture, 18, 21, 4, 25, accent);

            texture.Apply();
            return texture;
        }

        private static void FillRect(Texture2D texture, int x, int y, int width, int height, Color color)
        {
            for (int px = x; px < x + width; px++)
            {
                for (int py = y; py < y + height; py++)
                {
                    SetPixel(texture, px, py, color);
                }
            }
        }

        private static void FillTriangle(Texture2D texture, int x1, int y1, int x2, int y2, int x3, int y3, Color color)
        {
            int minX = Mathf.Min(x1, Mathf.Min(x2, x3));
            int maxX = Mathf.Max(x1, Mathf.Max(x2, x3));
            int minY = Mathf.Min(y1, Mathf.Min(y2, y3));
            int maxY = Mathf.Max(y1, Mathf.Max(y2, y3));

            float area = Edge(x1, y1, x2, y2, x3, y3);
            if (Mathf.Approximately(area, 0f))
            {
                return;
            }

            for (int x = minX; x <= maxX; x++)
            {
                for (int y = minY; y <= maxY; y++)
                {
                    float w1 = Edge(x2, y2, x3, y3, x, y) / area;
                    float w2 = Edge(x3, y3, x1, y1, x, y) / area;
                    float w3 = Edge(x1, y1, x2, y2, x, y) / area;
                    if (w1 >= 0f && w2 >= 0f && w3 >= 0f)
                    {
                        SetPixel(texture, x, y, color);
                    }
                }
            }
        }

        private static void DrawLine(Texture2D texture, int x1, int y1, int x2, int y2, Color color)
        {
            int dx = Mathf.Abs(x2 - x1);
            int dy = -Mathf.Abs(y2 - y1);
            int sx = x1 < x2 ? 1 : -1;
            int sy = y1 < y2 ? 1 : -1;
            int error = dx + dy;

            while (true)
            {
                SetPixel(texture, x1, y1, color);
                if (x1 == x2 && y1 == y2)
                {
                    break;
                }

                int doubledError = 2 * error;
                if (doubledError >= dy)
                {
                    error += dy;
                    x1 += sx;
                }

                if (doubledError <= dx)
                {
                    error += dx;
                    y1 += sy;
                }
            }
        }

        private static float Edge(int x1, int y1, int x2, int y2, int x, int y)
        {
            return (x - x1) * (y2 - y1) - (y - y1) * (x2 - x1);
        }

        private static void SetPixel(Texture2D texture, int x, int y, Color color)
        {
            if (x < 0 || x >= IconSize || y < 0 || y >= IconSize)
            {
                return;
            }

            texture.SetPixel(x, y, color);
        }
    }
}
