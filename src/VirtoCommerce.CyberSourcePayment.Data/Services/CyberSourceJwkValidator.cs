using System;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.IdentityModel.Tokens;
using VirtoCommerce.CyberSourcePayment.Core.Models;

namespace VirtoCommerce.CyberSourcePayment.Data.Services;

public class CyberSourceJwkValidator(HttpClient httpClient)
{
    public Task VerifyJwt(bool sandbox, string jwt)
    {
        var jwtParts = jwt.Split('.');
        if (jwtParts.Length != 3)
        {
            throw new ArgumentException("Invalid JWT format");
        }

        var headerBase64Url = jwtParts[0];

        var headerJson = Encoding.UTF8.GetString(Base64UrlDecode(headerBase64Url));

        var header = JsonSerializer.Deserialize<CaptureContextResponseHeader>(headerJson);
        if (header == null || string.IsNullOrEmpty(header.kid))
        {
            throw new InvalidOperationException("Missing 'kid' in JWT header");
        }

        return VerifyJwtInternal(sandbox, jwt, header);
    }

    private async Task VerifyJwtInternal(bool sandbox, string jwt, CaptureContextResponseHeader header)
    {
        var jwk = await GetPublicKeyFromHeader(sandbox, header.kid);

        var rsaParameters = new RSAParameters
        {
            Modulus = Base64UrlDecode(jwk.n),
            Exponent = Base64UrlDecode(jwk.e)
        };

        using var rsa = RSA.Create();
        rsa.ImportParameters(rsaParameters);

        var rsaSecurityKey = new RsaSecurityKey(rsa)
        {
            KeyId = jwk.kid
        };

        var validationParameters = new TokenValidationParameters
        {
            ValidateAudience = false,
            ValidateIssuer = false,
            ValidateLifetime = false,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = rsaSecurityKey,
            // ValidAlgorithms = [SecurityAlgorithms.HmacSha256]
        };

        var handler = new JwtSecurityTokenHandler();
        handler.ValidateToken(jwt, validationParameters, out _);
    }

    private static byte[] Base64UrlDecode(string input)
    {
        var output = input.Replace('-', '+').Replace('_', '/');
        switch (output.Length % 4)
        {
            case 0:
                break;
            case 2:
                output += "==";
                break;
            case 3:
                output += "=";
                break;
            default:
                throw new FormatException("Illegal base64url string!");
        }
        return Convert.FromBase64String(output);
    }

    private async Task<JWK> GetPublicKeyFromHeader(bool sandbox, string kid)
    {
        var environment = CyberSourcePaymentMethodOptions.Environment(sandbox);
        var url = $"https://{environment}/flex/v2/public-keys/{kid}";
        var responseString = await httpClient.GetStringAsync(url);

        var jwk = JsonSerializer.Deserialize<JWK>(responseString);
        if (jwk == null)
        {
            throw new InvalidOperationException("Failed to deserialize JWK from server response.");
        }

        return jwk;
    }

    private sealed class JWK
    {
        public string kty { get; set; }
        public string kid { get; set; }
        public string use { get; set; }
        public string n { get; set; }
        public string e { get; set; }
    }

    private sealed class CaptureContextResponseHeader
    {
        public string alg { get; set; }
        public string typ { get; set; }
        public string kid { get; set; }
    }

}
