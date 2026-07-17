using System.Text.Json.Serialization;

namespace Simetric.DTOs.ESign;

public sealed class BesAuthResponseDto
{
    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = string.Empty;

    [JsonPropertyName("token_type")]
    public string TokenType { get; set; } = string.Empty;

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }
}

public sealed class BesProductoDto
{
    public string Uuid { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string? CertificateUuid { get; set; }
    public string? ContainerUuid { get; set; }
    public string? ValidityUuid { get; set; }
    public string? ContificoCode { get; set; }
    public bool Active { get; set; }
    public bool Common { get; set; }
}

public sealed class BesStakeholderProductDto
{
    public string Uuid { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string ProductUuid { get; set; } = string.Empty;
    public string StakeholderUuid { get; set; } = string.Empty;
    public bool Active { get; set; }
}

public sealed class BesCertificateRequestDto
{
    public string? Uuid { get; set; }
    public string? IdentificationType { get; set; }
    public string? Identification { get; set; }
    public string? FingerprintCode { get; set; }
    public string? Names { get; set; }
    public string? LastName1 { get; set; }
    public string? LastName2 { get; set; }
    public DateTime? BirthDate { get; set; }
    public string? Nationality { get; set; }
    public string? Sex { get; set; }
    public string? PhoneNumber { get; set; }
    public string? Email { get; set; }
    public string? Email2 { get; set; }
    public string? Province { get; set; }
    public string? City { get; set; }
    public string? Address { get; set; }
    public bool HasFrontIdentification { get; set; }
    public bool HasBackIdentification { get; set; }
    public bool HasSelfie { get; set; }
    public bool HasRucFile { get; set; }
    public bool HasSeniorVideo { get; set; }
    public bool HasAppointment { get; set; }
    public bool HasAcceptanceAppointment { get; set; }
    public bool HasConstitution { get; set; }
    public bool HasManagerIdentification { get; set; }
    public bool HasAuthorization { get; set; }
    public bool HasAdditionalFile { get; set; }
    public DateTime? RequestDate { get; set; }
    public DateTime? ApprovedDate { get; set; }
    public string? Status { get; set; }
    public string? Token { get; set; }
    public string? UanatacaStatus { get; set; }
    public string? Comments { get; set; }
    public string? ProductUuid { get; set; }
    public string? StakeholderUuid { get; set; }
    public string? CreatedBy { get; set; }
    public bool Active { get; set; }
    public bool Countable { get; set; }
    public bool Renovation { get; set; }
    public string? OfferUuid { get; set; }
}

public sealed class BesArchivoAdjuntoDto
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Base64 { get; set; } = string.Empty;
}

public sealed class BesTokenInfoDto
{
    public string? ShippingTypeUuid { get; set; }
    public string? DeliveryMethod { get; set; }
    public string? Office { get; set; }
    public string? ContactName { get; set; }
    public string? ContactPhone { get; set; }
    public string? Province { get; set; }
    public string? City { get; set; }
    public string? MainStreet { get; set; }
    public string? HouseNumber { get; set; }
    public string? SecondaryStreet { get; set; }
    public string? Reference { get; set; }
    public string? RecipientIdentification { get; set; }
    public string? RecipientName { get; set; }
    public string? SerialToken { get; set; }
}

public sealed class BesCreateCertificateRequestDto
{
    public string IdentificationType { get; set; } = string.Empty;
    public string Identification { get; set; } = string.Empty;
    public string? FingerprintCode { get; set; }
    public string Names { get; set; } = string.Empty;
    public string LastName1 { get; set; } = string.Empty;
    public string? LastName2 { get; set; }
    public string BirthDate { get; set; } = string.Empty;
    public string Nationality { get; set; } = string.Empty;
    public string Sex { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string? PhoneNumber2 { get; set; }
    public string Email { get; set; } = string.Empty;
    public string? Email2 { get; set; }
    public string Province { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string ProductUuid { get; set; } = string.Empty;
    public string? OfferUuid { get; set; }
    public string? Ruc { get; set; }
    public string? Company { get; set; }
    public string? Department { get; set; }
    public string? Position { get; set; }
    public string? Reason { get; set; }
    public string? IdentificationTypeManager { get; set; }
    public string? IdentificationManager { get; set; }
    public string? NamesManager { get; set; }
    public string? LastNameManager { get; set; }
    public BesTokenInfoDto? TokenInfo { get; set; }
    public BesArchivoAdjuntoDto FrontIdentification { get; set; } = new();
    public BesArchivoAdjuntoDto BackIdentification { get; set; } = new();
    public BesArchivoAdjuntoDto Selfie { get; set; } = new();
    public BesArchivoAdjuntoDto? SeniorVideo { get; set; }
    public BesArchivoAdjuntoDto? RucFile { get; set; }
    public BesArchivoAdjuntoDto? Constitution { get; set; }
    public BesArchivoAdjuntoDto? Appointment { get; set; }
    public BesArchivoAdjuntoDto? AcceptanceAppointment { get; set; }
    public BesArchivoAdjuntoDto? Authorization { get; set; }
    public BesArchivoAdjuntoDto? ManagerIdentification { get; set; }
    public BesArchivoAdjuntoDto? AdditionalFile { get; set; }
}

public sealed class BesCreateCertificateResponseDto
{
    public bool Success { get; set; }
    public int? StatusCode { get; set; }
    public string? Location { get; set; }
    public string? ErrorMessage { get; set; }
    public string? ResponseBody { get; set; }
    public string? Uuid { get; set; }
}

public sealed class BesSolicitudOperacionResultadoDto
{
    public bool Success { get; set; }
    public bool Created { get; set; }
    public int? StatusCode { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? Uuid { get; set; }
    public string? ProviderStatus { get; set; }
    public string? ProviderStatusText { get; set; }
    public string? Location { get; set; }
    public string? ErrorBody { get; set; }
}
