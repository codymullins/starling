namespace Tessera.Layout;

public readonly record struct Size(double Width, double Height)
{
    public static Size Empty { get; } = new(0, 0);
}

public readonly record struct Rect(double X, double Y, double Width, double Height)
{
    public static Rect Empty { get; } = new(0, 0, 0, 0);

    public double Right => X + Width;
    public double Bottom => Y + Height;

    public Rect Translate(double dx, double dy) => new(X + dx, Y + dy, Width, Height);
}

public readonly record struct Edges(double Top, double Right, double Bottom, double Left)
{
    public static Edges Zero { get; } = new(0, 0, 0, 0);

    public double Vertical => Top + Bottom;
    public double Horizontal => Left + Right;
}
