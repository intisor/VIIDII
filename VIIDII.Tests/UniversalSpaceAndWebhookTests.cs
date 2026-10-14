using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VIIDII.Data;
using VIIDII.Endpoints;
using VIIDII.Models;
using Xunit;

namespace VIIDII.Tests;

public class UniversalSpaceAndWebhookTests
{
    private static WebApplicationFactory<Program> CreateApp() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Database:Provider", "Sqlite");
            builder.UseSetting(
                "ConnectionStrings:DefaultConnection",
                $"Data Source=file:viidii-universal-{Guid.NewGuid():N}?mode=memory&cache=shared");
            builder.UseEnvironment("Development");
        });

    [Fact]
    public void SpacePresets_HaveExpectedDefaults()
    {
        var academic = SpacePreset.GetMeta(SpacePreset.Academic);
        Assert.Equal("Lecturer", academic.HostLabel);
        Assert.Equal("Student", academic.AttendeeLabel);

        var professional = SpacePreset.GetMeta(SpacePreset.Professional);
        Assert.Contains("registration", professional.DefaultPrompt, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Candidate", professional.AttendeeLabel);

        var corporate = SpacePreset.GetMeta(SpacePreset.Corporate);
        Assert.Contains("shareholder", corporate.DefaultPrompt, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Chairman / Host", corporate.HostLabel);

        var masterclass = SpacePreset.GetMeta(SpacePreset.Masterclass);
        Assert.Contains("ticket", masterclass.DefaultPrompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PaymentWebhook_ParsesPaystackPayloadSuccessfully()
    {
        var paystackJson = """
        {
            "event": "charge.success",
            "data": {
                "id": 302948291,
                "amount": 7500000,
                "currency": "NGN",
                "customer": {
                    "id": 892834,
                    "first_name": "Babajide",
                    "last_name": "Sanwo",
                    "email": "jide@example.com",
                    "phone": "08031234567"
                }
            }
        }
        """;

        var customer = PaymentWebhookEndpoints.ParsePaymentPayload(paystackJson);

        Assert.NotNull(customer);
        Assert.Equal("08031234567", customer.Identifier);
        Assert.Equal("Babajide Sanwo", customer.Name);
        Assert.Equal("jide@example.com", customer.Email);
        Assert.Equal("paystack", customer.Gateway);
    }

    [Fact]
    public void PaymentWebhook_ParsesFlutterwavePayloadSuccessfully()
    {
        var flutterwaveJson = """
        {
            "event": "charge.completed",
            "data": {
                "id": 482910,
                "tx_ref": "tx-viidii-9921",
                "customer": {
                    "name": "Ngozi Okonjo",
                    "email": "ngozi@example.com",
                    "phone_number": "08098765432"
                }
            }
        }
        """;

        var customer = PaymentWebhookEndpoints.ParsePaymentPayload(flutterwaveJson);

        Assert.NotNull(customer);
        Assert.Equal("08098765432", customer.Identifier);
        Assert.Equal("Ngozi Okonjo", customer.Name);
        Assert.Equal("ngozi@example.com", customer.Email);
        Assert.Equal("flutterwave", customer.Gateway);
    }

    [Fact]
    public async Task PaymentWebhook_EnrollsCustomerIntoSpaceRoster()
    {
        using var app = CreateApp();
        using var client = app.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });

        var spaceCode = $"ICAN{Guid.NewGuid().ToString("N")[..4].ToUpperInvariant()}";
        var matricNo = $"LEC{Guid.NewGuid().ToString("N")[..8]}";

        // Register host
        await client.PostAsJsonAsync("/api/account/register", new
        {
            name = "ICAN Tutor",
            matricNo,
            password = "test-password-123"
        });

        // Create professional space
        var createResp = await client.PostAsJsonAsync("/api/classes", new
        {
            code = spaceCode,
            name = "ICAN Taxation Masterclass",
            presetType = SpacePreset.Professional
        });
        Assert.True(createResp.IsSuccessStatusCode);

        // Fire Paystack Webhook
        var paystackJson = $$"""
        {
            "event": "charge.success",
            "data": {
                "customer": {
                    "first_name": "Amina",
                    "last_name": "Yusuf",
                    "email": "amina@example.com",
                    "phone": "07011223344"
                }
            }
        }
        """;

        var webhookResp = await client.PostAsync(
            $"/api/v1/webhooks/payment/{spaceCode}",
            new StringContent(paystackJson, System.Text.Encoding.UTF8, "application/json"));

        Assert.True(webhookResp.IsSuccessStatusCode);

        // Verify database enrollment
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ViidiiDbContext>();
        var entry = await db.RosterEntries.FirstOrDefaultAsync(r => r.Identifier == "07011223344");

        Assert.NotNull(entry);
        Assert.Equal("Amina Yusuf", entry.DisplayName);
        Assert.Equal("paystack", entry.Source);
    }

    [Fact]
    public async Task ProgrammaticAttendeeApi_AddsAndExportsAttendees()
    {
        using var app = CreateApp();
        using var client = app.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });

        var spaceCode = $"CORP{Guid.NewGuid().ToString("N")[..4].ToUpperInvariant()}";
        var matricNo = $"HOST{Guid.NewGuid().ToString("N")[..8]}";

        await client.PostAsJsonAsync("/api/account/register", new
        {
            name = "Board Secretary",
            matricNo,
            password = "test-password-123"
        });

        await client.PostAsJsonAsync("/api/classes", new
        {
            code = spaceCode,
            name = "Annual General Meeting",
            presetType = SpacePreset.Corporate
        });

        // Add attendees programmatically
        var attendeesPayload = new[]
        {
            new { identifier = "SH-1001", name = "Emeka Okafor", phone = "08030000001", email = "emeka@shareholder.ng", grouping = "Tier 1" },
            new { identifier = "SH-1002", name = "Zainab Ahmed", phone = "08030000002", email = "zainab@shareholder.ng", grouping = "Tier 1" }
        };

        var postResp = await client.PostAsJsonAsync($"/api/v1/spaces/{spaceCode}/attendees", attendeesPayload);
        Assert.True(postResp.IsSuccessStatusCode);

        // Generate Vouchers
        var voucherResp = await client.PostAsJsonAsync($"/api/v1/spaces/{spaceCode}/vouchers", new
        {
            count = 5,
            prefix = "VIP"
        });
        Assert.True(voucherResp.IsSuccessStatusCode);

        // Download Attendance CSV
        var csvResp = await client.GetAsync($"/api/v1/spaces/{spaceCode}/attendance.csv");
        Assert.True(csvResp.IsSuccessStatusCode);
        Assert.Equal("text/csv", csvResp.Content.Headers.ContentType?.MediaType);

        var csvContent = await csvResp.Content.ReadAsStringAsync();
        Assert.Contains("SH-1001", csvContent);
        Assert.Contains("Emeka Okafor", csvContent);
        Assert.Contains("VIP-", csvContent);
    }
}
