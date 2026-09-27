using System;
namespace CinnabarSharp.Core.Models
{
	public interface ICanvas
	{
        int GetAllocatedWidth();
        int GetAllocatedHeight();
		IViewport Viewport { get; }
    }

	public interface IViewport
	{
		int GetAllocatedWidth();
		int GetAllocatedHeight();
        Adjustment GetHadjustment();
        Adjustment GetVadjustment();

    }

	public class Adjustment
	{
        public double PageSize { get; set; }
        public double StepIncrement { get; set; }
        public double Value { get; set; }
        public double PageIncrement { get; set; }
        public double Upper { get; set; }
        public double Lower { get; set; }
    }
}

