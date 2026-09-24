using UnityEngine;

public static class GridMaskRotator
{
    public enum Rotation { Deg0, Deg90, Deg180, Deg270 }

    public static (Vector2 size, bool[] mask) Rotate(Vector2 size, bool[] mask, Rotation rotation)
    {
        int width = (int)size.x;
        int height = (int)size.y;

        switch (rotation)
        {
            case Rotation.Deg0:
                return (size, mask);

            case Rotation.Deg90:
            {
                int newWidth = height;
                int newHeight = width;
                bool[] newMask = new bool[newWidth * newHeight];

                for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    int nx = height - 1 - y;
                    int ny = x;
                    newMask[ny * newWidth + nx] = mask[y * width + x];
                }
                return (new Vector2(newWidth, newHeight), newMask);
            }

            case Rotation.Deg180:
            {
                bool[] newMask = new bool[mask.Length];
                for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    int nx = width - 1 - x;
                    int ny = height - 1 - y;
                    newMask[ny * width + nx] = mask[y * width + x];
                }
                return (size, newMask);
            }

            case Rotation.Deg270:
            {
                int newWidth = height;
                int newHeight = width;
                bool[] newMask = new bool[newWidth * newHeight];

                for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    int nx = y;
                    int ny = width - 1 - x;
                    newMask[ny * newWidth + nx] = mask[y * width + x];
                }
                return (new Vector2(newWidth, newHeight), newMask);
            }
        }

        return (size, mask);
    }
}