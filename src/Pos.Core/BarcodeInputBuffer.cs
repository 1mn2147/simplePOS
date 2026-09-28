using System.Text;

namespace Pos.Core;

public enum BarcodeInputStatus
{
    Buffering,
    Completed,
    Ignored,
    Rejected,
}

public sealed record BarcodeInputResult(
    BarcodeInputStatus Status,
    string? Barcode = null,
    string? BufferedText = null);

public sealed class BarcodeInputBuffer
{
    private readonly StringBuilder _buffer = new();
    private readonly TimeSpan _maximumInterKeyDelay;
    private readonly TimeSpan _partialInputTimeout;
    private readonly string _terminatorKey;
    private readonly int _minimumLength;
    private DateTimeOffset? _lastInputAt;

    public BarcodeInputBuffer(
        TimeSpan maximumInterKeyDelay,
        TimeSpan partialInputTimeout,
        string terminatorKey = "Enter",
        int minimumLength = 3)
    {
        if (maximumInterKeyDelay <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumInterKeyDelay));
        }

        if (partialInputTimeout < maximumInterKeyDelay)
        {
            throw new ArgumentOutOfRangeException(
                nameof(partialInputTimeout),
                "부분 입력 만료 시간은 최대 키 간격보다 길어야 합니다.");
        }

        if (string.IsNullOrWhiteSpace(terminatorKey))
        {
            throw new ArgumentException("종료키가 필요합니다.", nameof(terminatorKey));
        }

        if (minimumLength < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumLength));
        }

        _maximumInterKeyDelay = maximumInterKeyDelay;
        _partialInputTimeout = partialInputTimeout;
        _terminatorKey = terminatorKey;
        _minimumLength = minimumLength;
    }

    public BarcodeInputBuffer(AppSettings settings)
        : this(
            TimeSpan.FromMilliseconds(settings?.ScannerMaximumInterKeyDelayMilliseconds
                ?? throw new ArgumentNullException(nameof(settings))),
            TimeSpan.FromMilliseconds(settings.ScannerPartialInputTimeoutMilliseconds),
            settings.ScannerTerminatorKey,
            settings.MinimumBarcodeLength)
    {
    }

    public string BufferedText => _buffer.ToString();

    public BarcodeInputResult PushKey(
        string key,
        DateTimeOffset occurredAt,
        bool textInputActive = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        if (textInputActive)
        {
            Reset();
            return new BarcodeInputResult(BarcodeInputStatus.Ignored);
        }

        var gap = _lastInputAt.HasValue ? occurredAt - _lastInputAt.Value : TimeSpan.Zero;
        if (gap < TimeSpan.Zero || gap > _partialInputTimeout)
        {
            Reset();
            gap = TimeSpan.Zero;
        }

        if (string.Equals(key, _terminatorKey, StringComparison.OrdinalIgnoreCase))
        {
            var candidate = _buffer.ToString();
            var isFastEnough = !_lastInputAt.HasValue || gap <= _maximumInterKeyDelay;
            Reset();
            return candidate.Length >= _minimumLength && isFastEnough
                ? new BarcodeInputResult(BarcodeInputStatus.Completed, candidate)
                : new BarcodeInputResult(BarcodeInputStatus.Rejected);
        }

        if (key.Length != 1 || char.IsControl(key[0]))
        {
            Reset();
            return new BarcodeInputResult(BarcodeInputStatus.Ignored);
        }

        if (_lastInputAt.HasValue && gap > _maximumInterKeyDelay)
        {
            _buffer.Clear();
        }

        _buffer.Append(key[0]);
        _lastInputAt = occurredAt;
        return new BarcodeInputResult(BarcodeInputStatus.Buffering, BufferedText: _buffer.ToString());
    }

    public BarcodeInputResult PushCharacter(
        char character,
        DateTimeOffset occurredAt,
        bool textInputActive = false)
    {
        var key = character is '\r' or '\n' ? _terminatorKey : character.ToString();
        return PushKey(key, occurredAt, textInputActive);
    }

    public void Reset()
    {
        _buffer.Clear();
        _lastInputAt = null;
    }
}
