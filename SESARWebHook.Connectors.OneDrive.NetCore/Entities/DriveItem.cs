using System.Text.Json;

namespace SESARWebHook.Connectors.OneDrive.Entities
{
  public class DriveItem
  {
    public string Id { get; set; }
    public string Name { get; set; }

    public DriveItem(JsonElement item)
    {
      this.Id = item.GetProperty("id").ToString();
      this.Name = item.GetProperty("name").ToString();
    }
  }
}
