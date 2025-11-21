using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MemoryTimeline.Data.Models;

/// <summary>
/// Represents an application setting.
/// </summary>
[Table("app_settings")]
public class AppSetting
{
    [Key]
    [Column("setting_key")]
    [MaxLength(100)]
    public string SettingKey { get; set; } = string.Empty;

    [Required]
    [Column("setting_value")]
    public string SettingValue { get; set; } = string.Empty;

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
