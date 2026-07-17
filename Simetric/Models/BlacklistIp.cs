using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Simetric.Models;

public partial class BlacklistIp
{
    [Key]
    public int Idblacklist { get; set; }

    public string Direccionip { get; set; } = null!;

    public string? Motivo { get; set; }

    public DateTime? Fechadebloqueo { get; set; }

    public bool? Espermanente { get; set; }
}
