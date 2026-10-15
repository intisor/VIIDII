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

    [Fact]
    public async Task SelfRegistrationAndPresetConfiguration_WorkSeamlessly()
    {
        using var app = CreateApp();
        using var client = app.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });

        using var scope = app.Services.CreateScope();
        var classService = scope.ServiceProvider.GetRequiredService<VIIDII.Services.ClassService>();
        var rosterService = scope.ServiceProvider.GetRequiredService<VIIDII.Services.RosterService>();
        var db = scope.ServiceProvider.GetRequiredService<ViidiiDbContext>();

        // 1. Create a Masterclass space
        var (space, err) = await classService.CreateAsync(
            code: $"BOOT{Guid.NewGuid().ToString("N")[..4].ToUpperInvariant()}",
            name: "Fullstack Engineering Masterclass",
            lecturerId: 1,
            presetType: SpacePreset.Masterclass,
            joinPrompt: "Your WhatsApp Phone",
            enableWatermark: true,
            paymentLink: "https://paystack.com/pay/fullstack-bootcamp",
            allowSelfRegistration: true);

        Assert.Null(err);
        Assert.NotNull(space);
        Assert.Equal("https://paystack.com/pay/fullstack-bootcamp", space.PaymentLink);
        Assert.True(space.EnableWatermark);

        // 2. Test Self-Registration / RSVP
        var (entry, regErr) = await classService.RegisterAttendeeAsync(
            classId: space.Id,
            identifier: "08123456789",
            name: "Tunde Ednut",
            email: "tunde@example.com",
            phone: "08123456789");

        Assert.Null(regErr);
        Assert.NotNull(entry);
        Assert.Equal("08123456789", entry.Identifier);
        Assert.Equal("Tunde Ednut", entry.DisplayName);
        Assert.Equal("rsvp", entry.Source);

        // 3. Update preset configuration & payment link
        var updated = await classService.UpdatePresetAsync(
            classId: space.Id,
            presetType: SpacePreset.Professional,
            joinPrompt: "Enter your Student Number",
            enableWatermark: false,
            paymentLink: "https://flutterwave.com/pay/bootcamp-update");

        Assert.True(updated);

        var refreshed = await classService.GetByIdAsync(space.Id);
        Assert.NotNull(refreshed);
        Assert.Equal("https://flutterwave.com/pay/bootcamp-update", refreshed.PaymentLink);
        Assert.Equal(SpacePreset.Professional, refreshed.PresetType);
        Assert.False(refreshed.EnableWatermark);
        Assert.NotNull(refreshed.RosterMappingJson);

        var count = await classService.GetStudentCountAsync(space.Id);
        Assert.Equal(1, count);

        // Verify RSVP attendee can be resolved on join even without name or with friendly name
        var resolved = await rosterService.ResolveAttendeeAsync(space.Id, "08123456789", null);
        Assert.NotNull(resolved);
        Assert.Equal("Tunde Ednut", resolved.DisplayName);

        // 4. Verify public registration page route renders
        var pageResp = await client.GetAsync($"/register/{refreshed.Code}");
        Assert.True(pageResp.IsSuccessStatusCode);

        // 5. Verify guest join page renders
        var joinResp = await client.GetAsync($"/join/{refreshed.Code}?id=08123456789&rsvp=1");
        Assert.True(joinResp.IsSuccessStatusCode);
    }
}

