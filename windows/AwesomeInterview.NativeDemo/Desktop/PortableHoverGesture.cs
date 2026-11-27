using System;

namespace AwesomeInterview.NativeDemo;

internal sealed class PortableHoverGesture
{
    const long DwellMilliseconds = 800;
    const int MovementTolerance = 5;
    int anchorX, anchorY;
    long? hoveredSince;
    bool interactive;
    (int X, int Y)? lockedPosition;

    public bool Update(bool overWindow, bool mouseBusy, int x, int y, long now)
    {
        if (lockedPosition is { } position)
        {
            if (overWindow && Math.Abs(x - position.X) <= MovementTolerance && Math.Abs(y - position.Y) <= MovementTolerance)
                return false;
            lockedPosition = null;
        }
        if (interactive)
        {
            if (!overWindow && !mouseBusy) Reset();
            return interactive;
        }
        if (!overWindow || mouseBusy)
        {
            hoveredSince = null;
            return false;
        }
        if (hoveredSince == null || Math.Abs(x - anchorX) > MovementTolerance || Math.Abs(y - anchorY) > MovementTolerance)
        {
            anchorX = x;
            anchorY = y;
            hoveredSince = now;
        }
        interactive = now - hoveredSince.Value >= DwellMilliseconds;
        return interactive;
    }

    public void Reset()
    {
        hoveredSince = null;
        interactive = false;
        lockedPosition = null;
    }

    public void WaitForMovement(int x, int y) => lockedPosition = (x, y);
}
