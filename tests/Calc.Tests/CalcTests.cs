using Xunit;

public class CalcTests
{
    [Fact]
    public void Add_Sums()
    {
        Assert.Equal(5, Calc.Add(2, 3));
    }

    [Fact]
    public void Add_Negatives()
    {
        Assert.Equal(0, Calc.Add(-4, 4));
    }

    [Fact]
    public void Median_OddLengthUnsorted()
    {
        Assert.Equal(2, Calc.Median([3, 1, 2]));
    }

    [Fact]
    public void Median_EvenLengthUnsorted()
    {
        Assert.Equal(2.5, Calc.Median([4, 1, 3, 2]));
    }

    [Fact]
    public void Median_EmptyThrows()
    {
        Assert.Throws<ArgumentException>(() => Calc.Median([]));
    }
}
