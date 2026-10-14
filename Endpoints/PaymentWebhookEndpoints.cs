using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VIIDII.Data;
using VIIDII.Models;
using VIIDII.Services;

namespace VIIDII.Endpoints;

/// <summary>
/// Instant Payment Webhook Receiver for Paystack, Flutterwave, and generic payment gateways.
/// Automatically enrolls verified paying customers into the space's roster in real time.
/// </summary>
public static class PaymentWebhookEndpoints
{
    public static void MapPaymentWebhookEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/webhooks/payment");

        group.MapPost("/{code}", async (
            string code,
            HttpContext http,
            ClassService classService,
            ViidiiDbContext context,
            ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("VIIDII.PaymentWebhook");

            var space = await classService.GetByCodeAsync(code);
            if (space is null)
            {
                logger.LogWarning("Payment webhook received for non-existent space code: {Code}", code);
                return Results.NotFound(new { error = $"Space '{code}' was not found." });
            }

            using var reader = new StreamReader(http.Request.Body, Encoding.UTF8);
            var raw = await reader.ReadToEndAsync();

            if (string.IsNullOrWhiteSpace(raw))
            {
                return Results.BadRequest(new { error = "Empty webhook payload." });
            }

            var customer = ParsePaymentPayload(raw);
            if (customer is null || string.IsNullOrWhiteSpace(customer.Identifier))
            {
                logger.LogWarning("Webhook payload could not be parsed or lacked identifier: {Raw}", raw.Length > 200 ? raw[..200] : raw);
                return Results.BadRequest(new { error = "Could not resolve customer phone or email from payment webhook." });
            }

            var cleanId = customer.Identifier.Trim();
            var cleanName = string.IsNullOrWhiteSpace(customer.Name) ? cleanId : customer.Name.Trim();
            var cleanEmail = customer.Email?.Trim();
            var cleanPhone = customer.Phone?.Trim();
            var grouping = customer.Tier ?? "Paid Customer";

            var existing = await context.RosterEntries
                .FirstOrDefaultAsync(r => r.ClassId == space.Id && r.Identifier == cleanId);

            if (existing is not null)
            {
                if (!string.IsNullOrWhiteSpace(customer.Name)) existing.DisplayName = cleanName;
                if (!string.IsNullOrWhiteSpace(cleanEmail)) existing.Email = cleanEmail;
                if (!string.IsNullOrWhiteSpace(cleanPhone)) existing.Phone = cleanPhone;
                existing.Grouping = grouping;
            }
            else
            {
                var entry = new RosterEntry
                {
                    ClassId = space.Id,
                    Identifier = cleanId,
                    DisplayName = cleanName,
                    Email = cleanEmail,
                    Phone = cleanPhone,
                    Grouping = grouping,
                    Source = customer.Gateway ?? "payment_webhook",
                    Role = RosterRole.Student,
                    ImportedAtUtc = DateTime.UtcNow
                };
                context.RosterEntries.Add(entry);
            }

            await context.SaveChangesAsync();

            logger.LogInformation("Successfully enrolled paying customer {Identifier} ({Name}) into space {SpaceCode}",
                cleanId, cleanName, space.Code);

            return Results.Ok(new
            {
                status = "success",
                message = "Customer successfully enrolled.",
                space = space.Code,
                identifier = cleanId,
                name = cleanName
            });
        });
    }

    public static PaymentCustomer? ParsePaymentPayload(string rawJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            var root = doc.RootElement;

            var evt = GetStringProp(root, "event");

            // 1. Paystack / Flutterwave standard webhook with "data.customer"
            if (root.TryGetProperty("data", out var data) && data.TryGetProperty("customer", out var customer))
            {
                var email = GetStringProp(customer, "email");
                var phone = GetStringProp(customer, "phone") ?? GetStringProp(customer, "phone_number");
                var firstName = GetStringProp(customer, "first_name");
                var lastName = GetStringProp(customer, "last_name");
                var rawName = GetStringProp(customer, "name");
                var fullName = !string.IsNullOrWhiteSpace(rawName)
                    ? rawName
                    : $"{firstName} {lastName}".Trim();

                var isFlutterwave = customer.TryGetProperty("phone_number", out _) || evt == "charge.completed";
                var gateway = isFlutterwave ? "flutterwave" : "paystack";
                var tier = isFlutterwave ? "Flutterwave Paid" : "Paystack Paid";

                // Prioritize phone number for Nigerian spaces; fall back to email
                var identifier = !string.IsNullOrWhiteSpace(phone) ? phone : email;

                if (!string.IsNullOrWhiteSpace(identifier))
                {
                    return new PaymentCustomer(
                        identifier,
                        string.IsNullOrWhiteSpace(fullName) ? email : fullName,
                        email,
                        phone,
                        tier,
                        gateway
                    );
                }
            }

            // 3. Generic webhook: { "identifier": "...", "email": "...", "phone": "...", "name": "..." }
            var genId = GetStringProp(root, "identifier") ?? GetStringProp(root, "phone") ?? GetStringProp(root, "email");
            if (!string.IsNullOrWhiteSpace(genId))
            {
                return new PaymentCustomer(
                    genId,
                    GetStringProp(root, "name") ?? genId,
                    GetStringProp(root, "email"),
                    GetStringProp(root, "phone"),
                    GetStringProp(root, "tier") ?? "Direct Webhook",
                    "webhook"
                );
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    private static string? GetStringProp(JsonElement element, string propName)
    {
        if (element.TryGetProperty(propName, out var p) && p.ValueKind == JsonValueKind.String)
        {
            return p.GetString();
        }
        return null;
    }

    public record PaymentCustomer(
        string Identifier,
        string? Name,
        string? Email,
        string? Phone,
        string? Tier,
        string? Gateway
    );
}
