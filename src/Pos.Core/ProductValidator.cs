using System.Text;

namespace Pos.Core;

public enum ProductValidationSeverity
{
    Warning,
    Error,
}

public sealed record ProductValidationIssue(
    string PropertyName,
    string Message,
    ProductValidationSeverity Severity);

public sealed record ProductValidationResult(IReadOnlyList<ProductValidationIssue> Issues)
{
    public bool IsValid => Issues.All(issue => issue.Severity != ProductValidationSeverity.Error);

    public bool HasWarnings => Issues.Any(issue => issue.Severity == ProductValidationSeverity.Warning);
}

public sealed class ProductValidator
{
    public const long MaximumPrice = 999_999_999;
    public const long HighPriceWarningThreshold = 10_000_000;

    public ProductValidationResult Validate(
        Product product,
        IEnumerable<Product>? existingProducts = null)
    {
        ArgumentNullException.ThrowIfNull(product);
        var issues = new List<ProductValidationIssue>();

        if (product.Id == Guid.Empty)
        {
            AddError(nameof(Product.Id), "상품 ID가 필요합니다.");
        }

        if (string.IsNullOrWhiteSpace(product.Name))
        {
            AddError(nameof(Product.Name), "상품명을 입력해야 합니다.");
        }

        if (product.Price < 0 || product.Price > MaximumPrice)
        {
            AddError(nameof(Product.Price), $"가격은 0~{MaximumPrice:N0}원 범위여야 합니다.");
        }
        else if (product.Price > HighPriceWarningThreshold)
        {
            AddWarning(nameof(Product.Price), $"가격이 {HighPriceWarningThreshold:N0}원을 초과합니다.");
        }

        if (product.Stock < 0)
        {
            AddError(nameof(Product.Stock), "재고는 0 이상이어야 합니다.");
        }

        if (product.SortOrder < 0)
        {
            AddError(nameof(Product.SortOrder), "정렬 순서는 0 이상이어야 합니다.");
        }

        if (existingProducts is not null)
        {
            var peers = existingProducts
                .Where(existing => existing.Id != product.Id && existing.IsActive)
                .ToArray();
            var barcode = NormalizeBarcode(product.Barcode);
            if (barcode is not null && peers.Any(existing =>
                    string.Equals(NormalizeBarcode(existing.Barcode), barcode, StringComparison.Ordinal)))
            {
                AddError(nameof(Product.Barcode), "이미 등록된 바코드입니다.");
            }

            var normalizedName = NormalizeText(product.Name);
            if (normalizedName.Length > 0 && peers.Any(existing =>
                    string.Equals(NormalizeText(existing.Name), normalizedName, StringComparison.OrdinalIgnoreCase)))
            {
                AddWarning(nameof(Product.Name), "같은 이름의 활성 상품이 이미 있습니다.");
            }
        }

        return new ProductValidationResult(issues.AsReadOnly());

        void AddError(string propertyName, string message)
        {
            issues.Add(new ProductValidationIssue(propertyName, message, ProductValidationSeverity.Error));
        }

        void AddWarning(string propertyName, string message)
        {
            issues.Add(new ProductValidationIssue(propertyName, message, ProductValidationSeverity.Warning));
        }
    }

    public static string NormalizeText(string? value)
    {
        return (value ?? string.Empty).Trim().Normalize(NormalizationForm.FormKC);
    }

    public static string? NormalizeBarcode(string? value)
    {
        var normalized = NormalizeText(value);
        return normalized.Length == 0 ? null : normalized;
    }
}
