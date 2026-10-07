namespace NexaCommerce.Data;

/// <summary>Holds the resolved connection string and whether initialisation succeeded.</summary>
public class DatabaseStatus
{
    public bool IsReady { get; set; }
    public string ConnectionString { get; set; }
    public string ServerName { get; set; }
    public string Error { get; set; }
    public List<string> Log { get; } = new();
    public DateTime? InitializedAt { get; set; }
    public bool SeededThisRun { get; set; }
}
