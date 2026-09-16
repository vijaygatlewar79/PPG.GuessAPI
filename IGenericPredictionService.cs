using PPG.GuessAPI.Models;

namespace PPG.GuessAPI;

public interface IGenericPredictionService
{
    GenericPredictionResult Predict(string seriesData, int period, int backtestSize);
}
