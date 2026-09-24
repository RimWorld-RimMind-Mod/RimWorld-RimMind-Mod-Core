namespace RimMind.Domain.Common
{
    /// <summary>
    /// Canonical RimWorld time conversion constants and helper methods.
    /// Eliminates raw magic numbers across Core and submodules.
    /// In RimWorld: 60 ticks = 1 real second (at 1x speed), 2500 ticks = 1 game hour, 60000 ticks = 1 game day.
    /// </summary>
    public static class RimMindTime
    {
        public const int TicksPerSecond = 60;
        public const int TicksPerHour = 2500;
        public const int TicksPerDay = 60000;
        public const int HoursPerDay = 24;

        public static float TicksToDays(int ticks) => (float)ticks / TicksPerDay;
        public static float TicksToHours(int ticks) => (float)ticks / TicksPerHour;
        public static int TicksToDay(int ticks) => ticks / TicksPerDay + 1;
        public static int DaysToTicks(float days) => (int)(days * TicksPerDay);
        public static int HoursToTicks(float hours) => (int)(hours * TicksPerHour);
    }
}
