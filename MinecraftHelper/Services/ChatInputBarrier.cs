using System;

namespace MinecraftHelper.Services;

// Minecraft czyta klawiaturę w ticku gry, a komunikaty okna odbiera raz na klatkę.
// Gdy klatka gry się przytnie, klawisz czatu (T) i pierwsze znaki komendy trafiają
// do tego samego ticka: czat otwiera się dopiero po nich, „/” przepada, a reszta komendy
// idzie na czat jako zwykła wiadomość. Stałe opóźnienie tego nie gwarantuje.
//
// Bariera wpuszcza tekst dopiero wtedy, gdy gra na pewno obsłużyła T w osobnym ticku:
//  1. potwierdzenie WM_NULL (SendMessageCallback) = gra odebrała już wcześniejsze wejście,
//  2. po co najmniej MinTickGap wysyłamy kolejny WM_NULL; po ostatnim potwierdzeniu każda
//     następna klatka zaczyna się zaległym tickiem, który otworzy czat, zanim gra odbierze
//     tekst wysłany od tej chwili.
// Wejście wysłane PostMessage jest w kolejce przed pierwszym WM_NULL — wystarczą
// 2 potwierdzenia. SendInput trafia do gry przez kolejkę systemową z opóźnieniem,
// więc dla gry na wierzchu potrzebne jest jedno potwierdzenie więcej.
// Czysta logika bez wywołań Windows — wysyłanie WM_NULL dostarcza wywołujący.
internal sealed class ChatInputBarrier
{
    public static readonly TimeSpan MinTickGap = TimeSpan.FromMilliseconds(120);
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private enum Phase { Idle, AwaitConfirmation, Gap }

    private readonly Func<long, bool> _requestRoundTrip;
    private readonly int _requiredConfirmations;
    private Phase _phase;
    private long _token;
    private int _confirmations;
    private DateTime _armedAtUtc;
    private DateTime _lastConfirmationAtUtc;

    public ChatInputBarrier(Func<long, bool> requestRoundTrip, int requiredConfirmations = 2)
    {
        _requestRoundTrip = requestRoundTrip;
        _requiredConfirmations = Math.Max(1, requiredConfirmations);
    }

    public bool TimedOut { get; private set; }

    // Wywołać zaraz po wysłaniu klawisza otwierającego czat.
    public void Arm(DateTime now)
    {
        _armedAtUtc = now;
        _confirmations = 0;
        TimedOut = false;
        _phase = Request() ? Phase.AwaitConfirmation : Phase.Idle;
    }

    public void Reset() => _phase = Phase.Idle;

    // Potwierdzenie WM_NULL z wątku gry; stare tokeny (np. po ponownym Arm) są pomijane.
    public void Acknowledge(long token, DateTime now)
    {
        if (token != _token || _phase != Phase.AwaitConfirmation)
            return;

        _confirmations++;
        _lastConfirmationAtUtc = now;
        _phase = _confirmations >= _requiredConfirmations ? Phase.Idle : Phase.Gap;
    }

    public bool IsReady(DateTime now)
    {
        if (_phase == Phase.Idle)
            return true;

        if (now - _armedAtUtc >= Timeout)
        {
            // Gra nie odpowiada: dalsze czekanie nic nie zmieni, a komunikaty
            // i tak zostaną przetworzone w kolejności wysłania.
            TimedOut = true;
            _phase = Phase.Idle;
            return true;
        }

        if (_phase == Phase.Gap && now - _lastConfirmationAtUtc >= MinTickGap)
            _phase = Request() ? Phase.AwaitConfirmation : Phase.Idle;

        return false;
    }

    private bool Request()
    {
        _token++;
        return _requestRoundTrip(_token);
    }
}
