namespace RimMind.Infrastructure.UI.DebugTables
{
    /// <summary>
    /// Shared contract for debug table page builders consumed polymorphically by table page infrastructure.
    /// </summary>
    public interface IDebugTableModelBuilder
    {
        DebugTableModel Build();
    }
}
