using System.ComponentModel;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;

public class EntryPoint
{
    private static int mMaxDepth = -1;
    private static long mCacheHits = 0;
    private static long mCacheMiss = 0;

    private static WikipediaRequests mRequests = new WikipediaRequests();

    private static List<string> mLinkTable = new();
    // Dictionary pointing from a string to an index in the link table.
    private static Dictionary<string, int> mLinksInTable = new();

    // Dictionary mapping each link to a list of its connections.
    private static Dictionary<int, HashSet<int>> mConnections = new();

    private static Regex mWikipediaLinkRegex = new
        Regex(@"<a href=""\/wiki\/(?:[^?#\/\s]+)(?=[?#""])");

    private static Regex mReferencesStartRegex = new
        Regex(@"(<span class=""mw-headline"" id=""References"">References</span>)|(<span class=""mw-headline"" id=""See_also"">)|(<span class=""mw-headline"" id=""Notes"">Notes</span>)");

    private static Regex mDocumentStartRege = new
        Regex(@"From Wikipedia, the free encyclopedia");

    private static string mStartingPage = "/wiki/Cat";
    private static string mEndingPage = "/wiki/adfasdfasdf";
    private const int mDepth = 4;
    private static string mConnectionsFileName = "./connections.txt";

    private static void AddLink(string url)
    {
        mLinkTable.Add(url);
        mLinksInTable[url] = mLinkTable.Count - 1;
    }

    private static void DumpConnections()
    {
        using (StreamWriter writer = new StreamWriter(mConnectionsFileName))
        {
            // Write link table.
            foreach (var link in mLinksInTable)
            {
                writer.Write(link.Key + " ");
            }
            writer.WriteLine();

            // Write connections in new format.
            foreach (var link in mConnections)
            {
                writer.Write(link.Key + " ");

                foreach (var value in link.Value)
                {
                    writer.Write(value + " ");
                }

                writer.WriteLine();
            }
        }
    }

    private static void LoadConnections()
    {
        if (!File.Exists(mConnectionsFileName))
        {
            return;
        }

        using (StreamReader reader = new StreamReader(new FileStream(mConnectionsFileName, FileMode.Open)))
        {
            // Load link table.
            string linkList = reader.ReadLine();
            int index = 0;

            while (true)
            {
                int endIndex = linkList.IndexOf(' ', index);

                // Break out if we are at the end.
                if (endIndex == -1)
                {
                    break;
                }

                string link = linkList.Substring(index, endIndex - index).Trim();

                AddLink(link);

                index = endIndex + 1;
            }

            // Load connection set.
            while (!reader.EndOfStream)
            {
                string line = reader.ReadLine();
                index = line.IndexOf(' ');

                // I'm ok with throwing if it fucks up.
                int key = int.Parse(line.Substring(0, index));

                // Move to the start of the first item.
                index++;

                mConnections[key] = new();

                while(true)
                {
                    int endIndex = line.IndexOf(' ', index);

                    if (endIndex == -1)
                    {
                        break;
                    }

                    mConnections[key].Add(int.Parse(line.Substring(index, endIndex - index).Trim()));
                    index = endIndex + 1;
                }
            }
        }
    }

    public static void Main(string[] args)
    {
        bool found = false;

        LoadConnections();

        if (!mLinksInTable.ContainsKey(mStartingPage))
        {
            AddLink(mStartingPage);
        }

        try
        {
            HashSet<int> currentDepthLinks = null;
            HashSet<int> nextDepthlinks = new();

            for (int i = 0; i < mDepth && !found; i++)
            {
                mMaxDepth = i;
                Console.WriteLine($"Processing depth {i + 1}");

                if (!ProcessDepth(currentDepthLinks, nextDepthlinks))
                {
                    currentDepthLinks = nextDepthlinks;
                    nextDepthlinks = new();
                }
                else
                {
                    found = true;
                }
            }

            if (found)
            {
                Console.WriteLine("Found a connection!\n");

                // Iterate through our data again, and find the fastest link.
                List<int> linkOrder = FindFastestLink().ToList();
                foreach (var value in linkOrder)
                {
                    Console.Write(mLinkTable[value] + " -> ");
                }
                Console.WriteLine(mEndingPage);
            }
        }
        catch
        {
            mConnectionsFileName += ".err";
        }
        finally
        {
            DumpConnections();
        }

        Console.WriteLine($"Cache ratio: {mCacheHits}:{mCacheMiss} ({(mCacheHits / (mCacheHits + (double)mCacheMiss) * 100)}%)");
    }

    /// <summary>
    /// Assuming we have found a link already, find the fastest one.
    /// </summary>
    /// <returns></returns>
    public static Stack<int> FindFastestLink()
    {
        Stack<int> result = new Stack<int>();
        Stack<int> fastestLink = null;

        // Dictionary mapping a link index to a depth to which it was searched.
        Dictionary<int, int> checkedNodes = new Dictionary<int, int>();

        int url = mLinksInTable[mStartingPage];
        int endingPage = mLinksInTable[mEndingPage];
        FindFastestLinkRecursive(url, result, ref fastestLink, checkedNodes, endingPage);

        return fastestLink;
    }

    public static bool FindFastestLinkRecursive(int url, Stack<int> result, ref Stack<int> fastestLink, Dictionary<int, int> checkedNodes, int endingPage)
    {
        if (!mConnections.ContainsKey(url) || result.Contains(url) ||
            result.Count() > mMaxDepth ||
            // Check to see if we have explored this node to max depth or not. 
            // If so, break out.
            (checkedNodes.ContainsKey(url) && checkedNodes[url] > mMaxDepth))
        {
            return false;
        }

        checkedNodes[url] = result.Count();
        result.Push(url);

        foreach (var link in mConnections[url])
        {
            if (link == endingPage)
            {
                if (fastestLink == null ||
                    result.Count() < fastestLink.Count())
                {
                    fastestLink = new Stack<int>(result);
                    return true;
                }
            }

            if (FindFastestLinkRecursive(link, result, ref fastestLink, checkedNodes, endingPage))
            {
                return true;
            }
        }

        result.Pop();
        return false;
    }

    public static bool ProcessDepth(HashSet<int> currentDepthLinks, HashSet<int> nextDepthLinks)
    {
        if(currentDepthLinks == null)
        {
            return FillLinksAtUrl(mLinksInTable[mStartingPage], nextDepthLinks);
        }

        //Console.WriteLine($"Processing page: {mLinkTable[depthLink]}");
        foreach(var depthLink in currentDepthLinks) 
        {
            if (FillLinksAtUrl(depthLink, nextDepthLinks))
            {
                return true;
            }
        }

        return false;
    }

    public static bool FillLinksAtUrl(int url, HashSet<int> nextDepthSubLinks)
    {
        if (mConnections.ContainsKey(url))
        {
            mCacheHits++;
            foreach (var connection in mConnections[url])
            {
                nextDepthSubLinks.Add(connection);
            }

            // See if the end page is in the set of connections.
            if (mLinksInTable.ContainsKey(mEndingPage))
            {
                return nextDepthSubLinks.Contains(mLinksInTable[mEndingPage]);
            }

            return false;
        }
        else
        {
            mCacheMiss++;
            mConnections[url] = new HashSet<int>();
        }

        Console.WriteLine($"Downloading page {mLinkTable[url]}");
        var result = mRequests.ReadPage(mLinkTable[url]);

        if (result == null)
        {
            return false;
        }

        StreamReader reader = new StreamReader(result);

        string document = reader.ReadToEnd();
        reader.Close();
        return FindWikipediaLinks(document, nextDepthSubLinks, mConnections[url], GetDocumentStart(document), GetReferencesStart(document));
    }

    public static int GetDocumentStart(string document)
    {
        return mDocumentStartRege.Match(document).Index;
    }

    public static int GetReferencesStart(string document)
    {
        return mReferencesStartRegex.Match(document).Index;
    }

    public static bool FindWikipediaLinks(string document, HashSet<int> subLinks, HashSet<int> cachcedSublinks, int documentStart, int referenceStart)
    {
        MatchCollection links = mWikipediaLinkRegex.Matches(document, documentStart);

        foreach (Match link in links)
        {
            if (link.Index < referenceStart)
            {
                string toAdd = link.Value.Substring(9);

                if (!toAdd.Contains("."))
                {
                    // Add it to the link table if it's not already there.
                    if (!mLinksInTable.ContainsKey(toAdd))
                    {
                        AddLink(toAdd);
                    }

                    subLinks.Add(mLinksInTable[toAdd]);
                    cachcedSublinks.Add(mLinksInTable[toAdd]);

                    // Return true if we found it.
                    if (toAdd == mEndingPage)
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }
}

public class WikipediaRequests
{
    private HttpClient mClient = new HttpClient();

    /// <summary>
    /// Constructor.
    /// </summary>
    public WikipediaRequests()
    {
    }

    /// <summary>
    /// Reads a page and returns the content as a string.
    /// </summary>
    /// <param name="url"></param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    public Stream ReadPage(string url)
    {
        HttpRequestMessage message = new HttpRequestMessage(HttpMethod.Get, "https://en.wikipedia.org/" + url);

        var response = mClient.Send(message);

        if (response.IsSuccessStatusCode)
        {
            return response.Content.ReadAsStream();
        }
        else
        {
            //throw new Exception($"Failed to get webpage. {response.StatusCode.ToString()}");
            return null;
        }
    }
}
