// Tiny calculator used by the Claude Code hooks demo.
//
// The tests in tests/Calc.Tests are RED on purpose — two seeded bugs.
// See README demo 3 for the scripted flow.
public static class Calc
{
    public static int Add(int a, int b) => a - b; // seeded bug: subtraction

    public static double Median(IReadOnlyList<double> values)
    {
        if (values.Count == 0)
        {
            throw new ArgumentException("median() of empty sequence", nameof(values));
        }

        var data = values; // seeded bug: never sorts
        var mid = data.Count / 2;
        if (data.Count % 2 == 1)
        {
            return data[mid];
        }
        return (data[mid - 1] + data[mid]) / 2;
    }
}
