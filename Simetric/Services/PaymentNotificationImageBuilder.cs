using System.Globalization;
using System.Net;
using System.Text;

namespace Simetric.Services;

public static class PaymentNotificationImageBuilder
{
    public static string CompraGenerada(string producto, decimal total, string detalle) =>
        Build(
            "COMPRA GENERADA",
            "Tu compra ha sido generada exitosamente",
            "Por favor, mantente pendiente de la confirmacion de tu pago.",
            producto,
            detalle,
            total,
            "PENDIENTE DE PAGO",
            ColorPorProducto(producto));

    public static string PagoAprobado(string producto, decimal total, string detalle, string mensaje) =>
        Build(
            "PAGO APROBADO",
            "Tu pago ha sido aprobado exitosamente",
            mensaje,
            producto,
            detalle,
            total,
            "PROCESADO CORRECTAMENTE",
            ColorPorProducto(producto));

    public static string SolicitudEnviada(string producto, decimal total, string detalle) =>
        Build(
            "SOLICITUD ENVIADA",
            "Tu solicitud de firma ha sido enviada exitosamente",
            "La solicitud quedó registrada y pendiente de verificar el pago.",
            producto,
            detalle,
            total,
            "PENDIENTE DE VERIFICACIÓN",
            ColorPorProducto(producto));

    private static string Build(
        string eyebrow,
        string title,
        string message,
        string producto,
        string detalle,
        decimal total,
        string status,
        string accent)
    {
        var totalTexto = total.ToString("C2", CultureInfo.GetCultureInfo("es-EC"));
        var titleLines = Wrap(title, 34).ToArray();
        var messageLines = Wrap(message, 64).ToArray();
        var detalleSeguro = WebUtility.HtmlEncode(detalle);
        var productoSeguro = WebUtility.HtmlEncode(producto);
        var esERubrica = EsERubrica(producto);
        var headerEnd = esERubrica ? "#065f46" : "#1e3a8a";
        var backgroundStart = esERubrica ? "#ecfdf5" : "#eef4ff";
        var backgroundEnd = esERubrica ? "#f0fdf4" : "#f8fbff";
        var decorativeFill = esERubrica ? "#d1fae5" : "#dbeafe";
        var eyebrowColor = esERubrica ? "#d1fae5" : "#dbeafe";
        var brandName = esERubrica ? "E-RUBRICA" : "E-FACT";
        const int titleY = 232;
        const int titleLineHeight = 38;
        var messageY = titleY + titleLines.Length * titleLineHeight + 12;
        var titleSvg = string.Join("", titleLines.Select((line, index) =>
            $"<text x=\"126\" y=\"{titleY + index * titleLineHeight}\" class=\"title\">{WebUtility.HtmlEncode(line)}</text>"));
        var messageSvg = string.Join("", messageLines.Select((line, index) =>
            $"<text x=\"126\" y=\"{messageY + index * 23}\" class=\"message\">{WebUtility.HtmlEncode(line)}</text>"));

        var svg = $$"""
            <svg xmlns="http://www.w3.org/2000/svg" width="900" height="500" viewBox="0 0 900 500">
              <defs>
                <linearGradient id="background" x1="0" y1="0" x2="1" y2="1">
                  <stop offset="0%" stop-color="{{backgroundStart}}"/><stop offset="100%" stop-color="{{backgroundEnd}}"/>
                </linearGradient>
                <linearGradient id="header" x1="0" y1="0" x2="1" y2="0">
                  <stop offset="0%" stop-color="{{accent}}"/><stop offset="100%" stop-color="{{headerEnd}}"/>
                </linearGradient>
                <filter id="shadow" x="-20%" y="-20%" width="140%" height="140%">
                  <feDropShadow dx="0" dy="12" stdDeviation="16" flood-color="#183b66" flood-opacity=".15"/>
                </filter>
              </defs>
              <rect width="900" height="500" fill="url(#background)"/>
              <circle cx="790" cy="48" r="120" fill="#ffffff" opacity=".55"/>
              <circle cx="80" cy="468" r="100" fill="{{decorativeFill}}" opacity=".55"/>
              <rect x="44" y="34" width="812" height="432" rx="30" fill="#ffffff" stroke="#ffffff" stroke-width="2" filter="url(#shadow)"/>
              <path d="M44 64a30 30 0 0 1 30-30h752a30 30 0 0 1 30 30v112H44z" fill="url(#header)"/>
              <rect x="44" y="172" width="812" height="4" fill="{{accent}}" opacity=".92"/>
              <circle cx="105" cy="99" r="31" fill="#ffffff" opacity=".2"/>
              <path d="M88 99l11 11 23-25" fill="none" stroke="#ffffff" stroke-width="8" stroke-linecap="round" stroke-linejoin="round"/>
              <text x="154" y="91" class="brand">{{brandName}}</text>
              <text x="154" y="119" class="eyebrow">{{WebUtility.HtmlEncode(eyebrow)}}</text>
              <rect x="92" y="202" width="4" height="126" rx="2" fill="{{accent}}" opacity=".22"/>
              <text x="126" y="204" class="label">DETALLE DE LA OPERACION</text>
              {{titleSvg}}
              {{messageSvg}}
              <rect x="126" y="360" width="648" height="66" rx="16" fill="#f8fafc" stroke="#e2e8f0"/>
              <text x="150" y="387" class="small-label">{{productoSeguro.ToUpperInvariant()}}</text>
              <text x="150" y="410" class="detail">{{detalleSeguro}}</text>
              <text x="748" y="387" text-anchor="end" class="small-label">TOTAL</text>
              <text x="748" y="412" text-anchor="end" class="amount">{{totalTexto}}</text>
              <line x1="126" y1="430" x2="774" y2="430" stroke="#e8eef5" stroke-width="1"/>
              <rect x="126" y="436" width="220" height="22" rx="11" fill="{{accent}}" opacity=".12"/>
              <text x="236" y="451" text-anchor="middle" class="status" fill="{{accent}}">{{WebUtility.HtmlEncode(status)}}</text>
              <text x="774" y="451" text-anchor="end" class="footer">Numerica Software</text>
              <style>
                .brand{font:800 25px Segoe UI,Arial,sans-serif;fill:#fff;letter-spacing:2px}
                .eyebrow{font:700 12px Segoe UI,Arial,sans-serif;fill:{{eyebrowColor}};letter-spacing:1.5px}
                .label{font:800 11px Segoe UI,Arial,sans-serif;fill:{{accent}};letter-spacing:1.3px}
                .small-label{font:700 11px Segoe UI,Arial,sans-serif;fill:#64748b;letter-spacing:1.2px}
                .title{font:800 32px Segoe UI,Arial,sans-serif;fill:#102a43}
                .message{font:400 16px Segoe UI,Arial,sans-serif;fill:#526579}
                .detail{font:600 16px Segoe UI,Arial,sans-serif;fill:#102a43}
                .amount{font:800 23px Segoe UI,Arial,sans-serif;fill:#102a43}
                .status{font:800 10px Segoe UI,Arial,sans-serif;letter-spacing:1px}
                .footer{font:600 12px Segoe UI,Arial,sans-serif;fill:#94a3b8}
              </style>
            </svg>
            """;

        return $"data:image/svg+xml;base64,{Convert.ToBase64String(Encoding.UTF8.GetBytes(svg))}";
    }

    private static string ColorPorProducto(string producto) =>
        EsERubrica(producto) ? "#059669" : "#2563eb";

    private static bool EsERubrica(string producto) =>
        producto.Contains("FIRMA", StringComparison.OrdinalIgnoreCase) ||
        producto.Contains("E-SIGN", StringComparison.OrdinalIgnoreCase) ||
        producto.Contains("E-RUBRICA", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> Wrap(string value, int maxLength)
    {
        var words = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var line = new StringBuilder();

        foreach (var word in words)
        {
            if (line.Length > 0 && line.Length + word.Length + 1 > maxLength)
            {
                yield return line.ToString();
                line.Clear();
            }

            if (line.Length > 0) line.Append(' ');
            line.Append(word);
        }

        if (line.Length > 0) yield return line.ToString();
    }
}
