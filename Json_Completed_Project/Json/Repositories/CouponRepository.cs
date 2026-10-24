using Json.Models;
using Json.Shared;
using System;
using System.Collections.Generic;
using System.Text;

namespace Json.Repositories;

public class CouponRepository
{

    private readonly JsonFileStore _jsonFileStore;

    public CouponRepository(JsonFileStore jsonFileStore)
    {
        _jsonFileStore = jsonFileStore;
    }

    public async Task<Coupon?> GetByCodeAsync(
        string code,
        CancellationToken ct)
    {
        await SimulatedDelay.WaitAsync(
            "CouponRepository -> Opening connection",
            200, 200, ct);

        await SimulatedDelay.WaitAsync(
            "CouponRepository -> Executing coupon query",
            300, 600, ct);

        var coupons = await _jsonFileStore
            .ReadAllAsync<Coupon>(DataFiles.Coupons, ct);

        var coupon = coupons.FirstOrDefault(
            c => string.Equals(
                c.Code,
                code,
                StringComparison.OrdinalIgnoreCase));

        if (coupon is null)
        {
            Logging.Info($"CouponRepository -> Coupon '{code}' not found.");
            return null;
        }

        Logging.Success($"CouponRepository -> Coupon '{code}' found.");

        return coupon;
    }

    public Coupon? GetByCode(string code)
    {
        var coupons = _jsonFileStore.ReadAll<Coupon>(DataFiles.Coupons);

        return coupons.FirstOrDefault<Coupon>(c => string.Equals(
            c.Code,
            code, 
            StringComparison.OrdinalIgnoreCase));
    }
}
