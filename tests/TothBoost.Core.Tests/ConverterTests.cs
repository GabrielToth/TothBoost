using System;
using System.Globalization;
using System.Windows;
using TothBoost.App;
using Xunit;

namespace TothBoost.Core.Tests
{
    public class ConverterTests
    {
        [Fact]
        public void InverseBoolConverter_ConvertsTrueToFalseAndViceVersa()
        {
            var conv = new InverseBoolConverter();
            var culture = CultureInfo.InvariantCulture;

            Assert.Equal(false, conv.Convert(true, typeof(bool), string.Empty, culture));
            Assert.Equal(true, conv.Convert(false, typeof(bool), string.Empty, culture));
            Assert.Equal(false, conv.ConvertBack(true, typeof(bool), string.Empty, culture));
            Assert.Equal(true, conv.ConvertBack(false, typeof(bool), string.Empty, culture));

            // Non-bool inputs
            Assert.False((bool)conv.Convert("not a bool", typeof(bool), string.Empty, culture));
        }

        [Fact]
        public void BoolToVisibilityConverter_ConvertsNormalAndInverted()
        {
            var conv = new BoolToVisibilityConverter();
            var culture = CultureInfo.InvariantCulture;

            // Normal
            Assert.Equal(Visibility.Visible, conv.Convert(true, typeof(Visibility), string.Empty, culture));
            Assert.Equal(Visibility.Collapsed, conv.Convert(false, typeof(Visibility), string.Empty, culture));

            // Inverted
            Assert.Equal(Visibility.Collapsed, conv.Convert(true, typeof(Visibility), "invert", culture));
            Assert.Equal(Visibility.Visible, conv.Convert(false, typeof(Visibility), "invert", culture));

            // ConvertBack throws NotSupportedException
            Assert.Throws<NotSupportedException>(() => conv.ConvertBack(Visibility.Visible, typeof(bool), string.Empty, culture));
        }
    }
}
