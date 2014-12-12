using Newtonsoft.Json.Linq;

namespace TableTale
{
  public class RowChange
  {
    public long SYS_CHANGE_VERSION;
    public string SYS_CHANGE_OPERATION;
    public string Database;
    public string Table;
    public JObject Row;
  }
}