using Torch;
using Torch.Views;

namespace Wormhole.PostgreSql;

public class Config : ViewModel
{
    [Display(Name = "Host", GroupName = "Connection")]
    public string Host { get; set; } = "localhost";
    [Display(Name = "Port", GroupName = "Connection")]
    public int Port { get; set; } = 5432;
    
    [Display(Name = "Username", GroupName = "Credentials")]
    public string Username { get; set; } = "PGUSER";
    [Display(Name = "Password", GroupName = "Credentials")]
    public string Password { get; set; } = "PGPASSWORD";

    [Display(Name = "Database", GroupName = "Credentials")]
    public string Database { get; set; } = "wormhole";
}
