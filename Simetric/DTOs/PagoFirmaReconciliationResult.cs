namespace Simetric.DTOs;

public sealed class PagoFirmaReconciliationResult
{
    public int Consulted { get; set; }
    public int Approved { get; set; }
    public int NotApproved { get; set; }
    public int NotFound { get; set; }
    public int Errors { get; set; }
    public int SentToUanataca { get; set; }
    public int UanatacaErrors { get; set; }
}
