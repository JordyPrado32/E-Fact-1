namespace Simetric.DTOs
{
    using System.Text.Json.Serialization;

    public class PagomediosRequest
    {
        [JsonPropertyName("integration")]
        public bool Integration { get; set; } = true;

        [JsonPropertyName("third")]
        public ThirdParty Third { get; set; } = new();

        [JsonPropertyName("generate_invoice")]
        public int GenerateInvoice { get; set; } = 0;

        [JsonPropertyName("description")]
        public string Description { get; set; } = "Pago de Firma Electronica";

        [JsonPropertyName("amount")]
        public decimal Amount { get; set; }

        [JsonPropertyName("amount_with_tax")]
        public decimal AmountWithTax { get; set; }

        [JsonPropertyName("amount_without_tax")]
        public decimal AmountWithoutTax { get; set; } = 0;

        [JsonPropertyName("tax_value")]
        public decimal TaxValue { get; set; }

        [JsonPropertyName("notify_url")]
        public string? NotifyUrl { get; set; }

        [JsonPropertyName("custom_value")]
        public string? CustomValue { get; set; }

        [JsonPropertyName("has_cards")]
        public int HasCards { get; set; } = 0;

        [JsonPropertyName("has_de_una")]
        public int HasDeUna { get; set; } = 1;

        [JsonPropertyName("has_paypal")]
        public int HasPaypal { get; set; } = 0;

        [JsonPropertyName("has_safetypay")]
        public bool HasSafetypay { get; set; } = false;
    }

    public class ThirdParty
    {
        [JsonPropertyName("document")]
        public string Document { get; set; } = string.Empty;

        [JsonPropertyName("document_type")]
        public string DocumentType { get; set; } = "05";

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("email")]
        public string Email { get; set; } = string.Empty;

        [JsonPropertyName("phones")]
        public string Phones { get; set; } = string.Empty;

        [JsonPropertyName("address")]
        public string Address { get; set; } = string.Empty;

        [JsonPropertyName("type")]
        public string Type { get; set; } = "Individual";
    }
}
