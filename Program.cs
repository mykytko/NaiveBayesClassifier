using System.Text;

const string clickbait = "resources/clickbait_data";
const string nonClickbait = "resources/non_clickbait_data";
const string datasetFolder = "datasets";

const int basicSize = 100;
const int maximumSize = 32000;
const int multiplier = 2;

var delimiters = new[]
{
    ' ', '"', ','
    //' ', ';', '!', '?', '.', ',', ':', '"', '/', '\\', '{', '}', '(', ')', '[', ']', '%', '~',
    //'0', '1', '2', '3', '4', '5', '6', '7', '8', '9', '<', '>', '$', '#', '+', '*', '^', '='
};

int CountEntries(string filename)
{
    var entries = 0;
    var streamReader = new StreamReader(File.Open(filename, FileMode.Open));
    while (!streamReader.EndOfStream)
    {
        var line = streamReader.ReadLine();
        if (line == null)
        {
            break;
        }

        if (line == string.Empty)
        {
            continue;
        }
    
        entries++;
    }
    streamReader.Close();
    
    return entries;
}

bool CreateDataset(string sourceFileName, int size, Stream trainingDataFile, Stream testingDataFile, string identifier)
{
    var streamReader = new StreamReader(File.Open(sourceFileName, FileMode.Open));
    var iterator = 0;
    while (!streamReader.EndOfStream)
    {
        var line = streamReader.ReadLine();
            
        if (iterator >= size)
        {
            break;
        }
            
        switch (line)
        {
            case null:
                return false;
            case "":
                continue;
        }
        
        if (iterator < 2 * size / 3)
        {
            trainingDataFile.Write(Encoding.UTF8.GetBytes(identifier + " " + line + Environment.NewLine));
        }
        else
        {
            testingDataFile.Write(Encoding.UTF8.GetBytes(identifier + " " + line + Environment.NewLine));
        }

        iterator++;
    }
    streamReader.Close();
    return true;
}

int CreateDatasets(double clickbaitEntriesPercentage, double nonClickbaitEntriesPercentage)
{
    var currentSize = basicSize;
    while (true)
    {
        Directory.CreateDirectory(datasetFolder + "/" + currentSize);
        var basePath = datasetFolder + "/" + currentSize;
        var trainingDataFile = File.Open(basePath + "/trainingData", FileMode.Create);
        var testingDataFile = File.Open(basePath + "/testingData", FileMode.Create);

        var clickbaitSize = (int) Math.Floor(currentSize * clickbaitEntriesPercentage);
        var nonClickbaitSize = (int) Math.Floor(currentSize * nonClickbaitEntriesPercentage);
    
        // divide headers among training and testing datasets
        if (!CreateDataset(clickbait, clickbaitSize, 
                trainingDataFile, testingDataFile, "clickbait")
            || !CreateDataset(nonClickbait, nonClickbaitSize,
                trainingDataFile, testingDataFile, "non-clickbait"))
        {
            trainingDataFile.Close();
            testingDataFile.Close();
            Directory.Delete(basePath);
            return currentSize / 2;
        }
        
        testingDataFile.Close();
        trainingDataFile.Close();

        // double the size
        currentSize *= multiplier;
        if (currentSize < maximumSize) continue;
        return currentSize / 2;
    }
}

double CalculateNormalProbability(int count, double wordProbability, double classProbability)
{
    return (count * wordProbability + classProbability) / (count + 1);
}

void CalculateNormalProbabilities(
    IReadOnlyDictionary<string, int> clickbaitWordFrequencies,
    IReadOnlyDictionary<string, int> nonClickbaitWordFrequencies,
    double clickbaitProbability, 
    double nonClickbaitProbability,
    Stream frequenciesDebugFile,
    out Dictionary<string, double> clickbaitWordNormalProbabilities, 
    out Dictionary<string, double> nonClickbaitWordNormalProbabilities)
{
    clickbaitWordNormalProbabilities = new Dictionary<string, double>();
    nonClickbaitWordNormalProbabilities = new Dictionary<string, double>();
    foreach (var word in clickbaitWordFrequencies.Keys)
    {
        var clickbaitCount = clickbaitWordFrequencies[word];
        var nonClickbaitCount = nonClickbaitWordFrequencies[word];
        var totalCount = clickbaitCount + nonClickbaitCount;
        var clickbaitWordProbability = (double) clickbaitCount / totalCount;
        var nonClickbaitWordProbability = (double) nonClickbaitCount / totalCount;
        var clickbaitWordNormalProbability = CalculateNormalProbability(
            totalCount, clickbaitWordProbability, 
            clickbaitProbability);
        var nonClickbaitWordNormalProbability = CalculateNormalProbability(
            totalCount, nonClickbaitWordProbability, 
            nonClickbaitProbability);
        
        clickbaitWordNormalProbabilities[word] = clickbaitWordNormalProbability;
        nonClickbaitWordNormalProbabilities[word] = nonClickbaitWordNormalProbability;
        
        // "Word,Clickbait,Non-clickbait,Clickbait probability,Non-Clickbait probability," +
        // "Clickbait normal probability,Non-clickbait normal probability"
        frequenciesDebugFile.Write(Encoding.UTF8.GetBytes(
            $"\"{word}\",{clickbaitCount},{nonClickbaitCount}," +
            $"{clickbaitWordProbability:F3},{nonClickbaitWordProbability:F3}," +
            $"{clickbaitWordNormalProbability:F3},{nonClickbaitWordNormalProbability:F3}"
            + Environment.NewLine));
    }
}

void CollectData(Stream fileStream, 
    out Dictionary<string, int> clickbaitWordFrequencies, 
    out Dictionary<string, int> nonClickbaitWordFrequencies,
    out int clickbaitEntries, out int nonClickbaitEntries)
{
    clickbaitWordFrequencies = new Dictionary<string, int>();
    nonClickbaitWordFrequencies = new Dictionary<string, int>();
    
    var streamReader = new StreamReader(fileStream);
    clickbaitEntries = 0;
    nonClickbaitEntries = 0;
    while (!streamReader.EndOfStream)
    {
        var line = streamReader.ReadLine();
        if (line == null)
        {
            break;
        }

        if (line == string.Empty)
        {
            continue;
        }

        Tokenize(line, out var type, out var tokens);
        Dictionary<string, int>? wordFrequencies;
        switch (type)
        {
            case "clickbait":
                clickbaitEntries++;
                wordFrequencies = clickbaitWordFrequencies;
                break;
            case "non-clickbait":
                nonClickbaitEntries++;
                wordFrequencies = nonClickbaitWordFrequencies;
                break;
            default:
                throw new InvalidDataException();
        }

        foreach (var word in tokens)
        {
            if (!wordFrequencies.ContainsKey(word))
            {
                clickbaitWordFrequencies[word] = 0;
                nonClickbaitWordFrequencies[word] = 0;
            }

            wordFrequencies[word] += 1;
        }
    }
}

void Tokenize(string line, out string type, out string[] tokens)
{
    type = line.Split(' ')[0];
    tokens = line.Remove(0, type.Length).Split(delimiters,
        StringSplitOptions.RemoveEmptyEntries);
    for (var i = 0; i < tokens.Length; i++)
    {
        tokens[i] = tokens[i].ToLower();

        if (tokens[i].EndsWith('\'') || tokens[i].EndsWith('-'))
        {
            tokens[i] = tokens[i].Remove(tokens[i].Length - 1);
        }

        if (tokens[i].StartsWith('\'') || tokens[i].StartsWith('-'))
        {
            tokens[i] = tokens[i].Remove(0, 1);
        }
    }
}

string TestModel(
    double clickbaitProbability, 
    double nonClickbaitProbability, 
    IReadOnlyDictionary<string, double> clickbaitWordNormalProbabilities,
    IReadOnlyDictionary<string, double> nonClickbaitWordNormalProbabilities,
    Stream trainingDataFile, 
    Stream testingDataFile, 
    Stream testingDebugFile, 
    Stream frequenciesDebugFile, 
    int iteration)
{
    var streamReader = new StreamReader(testingDataFile);
    var successCounter = 0;
    var failureCounter = 0;
    while (!streamReader.EndOfStream)
    {
        var line = streamReader.ReadLine();
        var clickbaitFormula = string.Empty;
        var nonClickbaitFormula = string.Empty;
        if (line == null)
        {
            break;
        }

        Tokenize(line, out var type, out var tokens);
        var localClickbaitProbability = clickbaitProbability;
        var localNonClickbaitProbability = nonClickbaitProbability;
        for (var j = 0; j < tokens.Length; j++)
        {
            var word = tokens[j];
            var wordClickbaitProbability = clickbaitProbability;
            var wordNonClickbaitProbability = nonClickbaitProbability;

            if (clickbaitWordNormalProbabilities.ContainsKey(word))
            {
                wordClickbaitProbability = clickbaitWordNormalProbabilities[word];
            }

            if (nonClickbaitWordNormalProbabilities.ContainsKey(word))
            {
                wordNonClickbaitProbability = nonClickbaitWordNormalProbabilities[word];
            }

            localClickbaitProbability *= wordClickbaitProbability;
            localNonClickbaitProbability *= wordNonClickbaitProbability;

            if (j != 0)
            {
                clickbaitFormula += "*";
                nonClickbaitFormula += "*";
            }

            clickbaitFormula += $"{wordClickbaitProbability:F3}";
            nonClickbaitFormula += $"{wordNonClickbaitProbability:F3}";
        }

        var success = type switch
        {
            "clickbait" => localClickbaitProbability >= localNonClickbaitProbability,
            "non-clickbait" => localClickbaitProbability < localNonClickbaitProbability,
            _ => throw new InvalidDataException()
        };

        // "Header,Clickbait formula,Non-clickbait formula,Clickbait,Non-clickbait,Success"
        testingDebugFile.Write(Encoding.UTF8.GetBytes(
            $"\"{line.Replace("\"", "\"\"")}\"," +
            $"\"{clickbaitFormula}\"," +
            $"\"{nonClickbaitFormula}\"," +
            $"{localClickbaitProbability:F3}," +
            $"{localNonClickbaitProbability:F3}," +
            $"{(success ? "success" : "failure")}"
            + Environment.NewLine));

        if (success)
        {
            successCounter++;
        }
        else
        {
            failureCounter++;
        }
    }

    trainingDataFile.Close();
    testingDataFile.Close();
    frequenciesDebugFile.Close();
    testingDebugFile.Close();

    return $"For dataset {iteration}: {successCounter} successful guesses, {failureCounter} + unsuccessful guesses, " +
           $"overall success rate is {(double) successCounter / (successCounter + failureCounter)}." 
           + Environment.NewLine;
}

// count entries
var clickbaitEntries = CountEntries(clickbait);
var nonClickbaitEntries = CountEntries(nonClickbait);

// get clickbait and non-clickbait percentages
var clickbaitEntriesPercentage = (double) clickbaitEntries / (clickbaitEntries + nonClickbaitEntries);
var nonClickbaitEntriesPercentage = (double) nonClickbaitEntries / (clickbaitEntries + nonClickbaitEntries);

// create datasets of different sizes
var realMaximumSize = CreateDatasets(clickbaitEntriesPercentage, nonClickbaitEntriesPercentage);

// train a model on every dataset and then test each of them
var resultingData = string.Empty;
for (var i = basicSize; i <= realMaximumSize; i *= 2)
{
    var basePath = datasetFolder + "/" + i;
    
    // debug files
    var frequenciesDebugFile = File.Open(basePath + "/frequencies.csv", FileMode.Create);
    frequenciesDebugFile.Write(Encoding.UTF8.GetBytes(
        "Word,Clickbait,Non-clickbait,Clickbait probability,Non-Clickbait probability," +
        "Clickbait normal probability,Non-clickbait normal probability" + Environment.NewLine));
    
    var testingDebugFile = File.Open(basePath + "/testing.csv", FileMode.Create);
    testingDebugFile.Write(Encoding.UTF8.GetBytes(
        "Header,Clickbait formula,Non-clickbait formula,Clickbait,Non-clickbait,Success" + Environment.NewLine));
    
    var trainingDataFile = File.Open(basePath + "/trainingData", FileMode.Open);
    var testingDataFile = File.Open(basePath + "/testingData", FileMode.Open);
    
    // train
    // correspondence of each word to it's frequency in clickbait and non-clickbait headers respectively
    CollectData(
        trainingDataFile, 
        out var clickbaitWordFrequencies, 
        out var nonClickbaitWordFrequencies, 
        out var localClickbaitEntries, 
        out var localNonClickbaitEntries);

    var localTotalEntries = localClickbaitEntries + localNonClickbaitEntries;
    var clickbaitProbability = (double) localClickbaitEntries / localTotalEntries;
    var nonClickbaitProbability = (double) localNonClickbaitEntries / localTotalEntries;
    
    CalculateNormalProbabilities(
        clickbaitWordFrequencies, 
        nonClickbaitWordFrequencies, 
        clickbaitProbability, nonClickbaitProbability, frequenciesDebugFile, 
        out var clickbaitWordNormalProbabilities, 
        out var nonClickbaitWordNormalProbabilities);

    // test model
    resultingData += TestModel(clickbaitProbability, nonClickbaitProbability, 
        clickbaitWordNormalProbabilities, nonClickbaitWordNormalProbabilities, 
        trainingDataFile, testingDataFile, testingDebugFile,  frequenciesDebugFile, i);
}

// display the map with resulting data
Console.WriteLine(resultingData);