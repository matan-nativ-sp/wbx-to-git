using System;
using System.IO;
using System.Diagnostics;

using WBX.whiteOPS.ServerCore;
using WBX.whiteOPS.Tools.PerformanceMonitorHelper;
using WBX.whiteOPS.ServerCore.DataTypes;
using WBXWPCDataRequestGenerator.WPCFrameworkService;
using System.Threading;

namespace WBXWPCDataRequestGenerator {

    class Program {

        private const string USER_NAME = "user1";
        private const string CLIENT_IP = "192.168.2.116";
        private const string AD_UID = "ADWPC";
        private const string CP_UID = "CPWPC";
        private const string EPO_UID = "ePOWPC";

        private const string DATA_COLLECTOR_FILE_HEADER_LINE = 
            "timestamp (" + Constants.TIMESTAMP_FORMAT + "),milliseconds to process request";
        private const string DATA_COLLECTOR_FILE_DATA_LINE = "{0},{1}";
        private const string DATA_COLLECTOR_FILE_ERROR_LINE = "{0},ERROR: {1}";

        private static WPCFrameworkServiceClient _serviceClient;
        private static PerformanceCounter[] _counters = null;
        private static StreamWriter[] _streamWriters = null;

        private static int _threadsCount;
        private static int _threadsAreInitialized = 0;
        private static Int64 _numberOfThreadsFinished = 0;
        
        private delegate void _runTestCase(
            string agentUID,
            string ipAddress,
            string username,
            PerformanceCounter counter,
            StreamWriter sw,
            int iterationsCount,
            int delayTime
        );

        static void Main(string[] args) {

            #region Constants

            string PROCESS_ID = Process.GetCurrentProcess().Id.ToString();
            const string COUNTER_NAME = "WPC Data Request Generator";
            const string COUNTER_HELP = "WPC Data Request Generator is used to monitor the number of seconds each request took until it got back from the WPC framework service.";

            #endregion

            #region Validate arguments count

            // Check number of arguments
            if (args.Length != 4) {
                Console.WriteLine("Usage: WBXEventGenerator.exe [test number] [iterations] [threads] [delay]\n");
                Console.WriteLine("\ttest number: the test number to run");
                Console.WriteLine("\titeration: the number of times to perform the operations\n\t\tin each thread");
                Console.WriteLine("\tthreads number: the number of threads that will execute\n\t\tthe operations simultaneously");
                Console.WriteLine("\tdelay: number of milliseconds to wait between each operations");
                return;
            }

            #endregion

            #region Initialize local variables

            int testNumber;
            int delayTime;
            int iterationsCount;

            #endregion

            #region Validations

            // Check the test number
            if (!int.TryParse(args[0], out testNumber)) {
                Console.WriteLine("Invalid test number " + args[0]);
                return;
            }

            // Check the iterations count
            if (!int.TryParse(args[1], out iterationsCount)) {
                Console.WriteLine("Invalid iterations count number " + args[1]);
                return;
            }
            else {
                if (iterationsCount < 1) {
                    Console.WriteLine("Iterations count number should be at least 1");
                    return;
                }
            }

            // Check the threads count number
            if (!int.TryParse(args[2], out _threadsCount)) {
                Console.WriteLine("Invalid thread count number " + args[2]);
                return;
            }
            else {
                if (_threadsCount < 1) {
                    Console.WriteLine("Threads number should be at least 1");
                    return;
                }
            }

            // Check delay time
            if (!int.TryParse(args[3], out delayTime)) {
                Console.WriteLine("Invalid delay number " + args[3]);
                return;
            }

            #endregion

            #region Create and open the agent configuration service

            _serviceClient =
                new WPCFrameworkServiceClient();

            try {
                _serviceClient.Open();
            }
            catch (Exception ex) {
                Console.Out.Write(
                    "Open WPC Framework Service failed with exception: " + ex
                );
                return;
            }

            #endregion

            #region Create performance counters

            // Delete the WhiteOPS category
            PerformanceMonitorHelper.deleteCategory(
                PerformanceMonitorHelper.WBX_CATEGORY_NAME
            );

            string counterInstanceName = COUNTER_NAME + " " + PROCESS_ID;
            _counters = new PerformanceCounter[_threadsCount];

            for (int i = 0; i < _threadsCount; i++) {

                _counters[i] =
                    createCounter(
                        COUNTER_NAME,
                        COUNTER_HELP,
                        counterInstanceName + ", thread " + i.ToString()
                    );
            }

            #endregion

            #region Create the stream writers
            
            _streamWriters = new StreamWriter[_threadsCount];

            for (int i = 0; i < _threadsCount; i++) {

                _streamWriters[i] =
                    openFile(
                        counterInstanceName + "_thread_" + i.ToString()
                    );
            }

            #endregion

            ///////////////////////////////////////////////////////
            /// Before starting to handle the events, pause the
            /// process and wait for the user to to hit any key to
            /// continue. This is done is order to let the user
            /// create a new Performance Counter Log.
            ///////////////////////////////////////////////////////

            // Show the user the counter instance name
            Console.WriteLine(
                "New performance counters were created, the counter instance name is:\n{0}",
                counterInstanceName
            );
            Console.WriteLine("Press any key to process tests...");
            Console.ReadKey(true);
            Console.WriteLine(
                "\nProcessing operations of test {0}, please wait...\n",
                testNumber
            );

            string agentUID = null;
            _runTestCase runTestDelegate = null;

            switch (testNumber) {
                case 1:
                    agentUID = AD_UID;
                    break;
                case 2:
                    agentUID = EPO_UID;
                    break;
                case 3:
                    agentUID = CP_UID;
                    break;
                default:
                    Console.WriteLine(
                        "Test number {0} does not exist.",
                        testNumber
                    );
                    return;
            }

            runTestDelegate = new _runTestCase(runReqeusts);

            if (runTestDelegate != null) {

                // For each counter begin a new thread
                for (int i = 0; i < _threadsCount; i++) {

                    runTestDelegate.BeginInvoke(
                        agentUID,
                        CLIENT_IP,
                        USER_NAME,
                        _counters[i],
                        _streamWriters[i],
                        iterationsCount,
                        delayTime,
                        null,
                        null
                    );
                }
            }

            // Wait for all threads to finished.
            while (_numberOfThreadsFinished != _threadsCount) {
                Thread.Sleep(1000);
            }

            #region Remove the counter instances and close data collector files
            if (_counters != null) {

                Console.WriteLine("\nPress any key to terminate performance instance counters...");
                Console.ReadKey(true);

                foreach (PerformanceCounter counter in _counters) {
                    counter.RemoveInstance();
                }

                foreach (StreamWriter sw in _streamWriters) {
                    sw.Close();
                }
            }
            #endregion

            Console.WriteLine("Process Finished");
        }

        private static PerformanceCounter createCounter(
            string COUNTER_NAME,
            string COUNTER_HELP,
            string instanceName
        ) {
            PerformanceCounter counter = null;

            PerformanceMonitorHelper.createCounter(
                PerformanceMonitorHelper.WBX_CATEGORY_NAME,
                PerformanceMonitorHelper.WBX_CATEGORY_NAME,
                PerformanceCounterCategoryType.MultiInstance,
                COUNTER_NAME,
                COUNTER_HELP,
                PerformanceCounterType.NumberOfItems64
            );

            counter =
                PerformanceMonitorHelper.getCounter(
                    PerformanceMonitorHelper.WBX_CATEGORY_NAME,
                    COUNTER_NAME,
                    instanceName,
                    PerformanceCounterInstanceLifetime.Process,
                    false
                );
            return counter;
        }


        private static string createEventXMLForWPCDataFetch(            
            string dateTime,
            string ipAddress,
            string username
        ) {

            WBXXml xmlResult = new WBXXml();

            // Add Policy Data Element
            xmlResult.addElement(
                null,
                Constants.WBX_XML.XML_POLICY_DATA_ELEMENT_NAME
            );

            // Add the timestamp property
            xmlResult.addPair(
                Constants.BASE_EVENT_XML_FIELDS.TIME_STAMP.FATHER_ELEMENT,
                Constants.BASE_EVENT_XML_FIELDS.TIME_STAMP.NAME,
                dateTime
            );

            // Add the IP Address property
            xmlResult.addPair(
                Constants.BASE_EVENT_XML_FIELDS.IP_ADDRESS.FATHER_ELEMENT,
                Constants.BASE_EVENT_XML_FIELDS.IP_ADDRESS.NAME,
                ipAddress
            );

            // Add the username property
            xmlResult.addPair(
                Constants.BASE_EVENT_XML_FIELDS.USER_NAME.FATHER_ELEMENT,
                Constants.BASE_EVENT_XML_FIELDS.USER_NAME.NAME,
                username
            );

            return xmlResult.ToString();
        }

        private static StreamWriter openFile(
            string fileName
        ) {
            
            FileStream fs =
                new FileStream(
                    fileName+".csv",
                    FileMode.OpenOrCreate,
                    FileAccess.Write
                );
            StreamWriter sw = new StreamWriter(fs);
            
            return sw;
        }

        private static Int64 calculateTimeInMilliseconds(
            TimeSpan timeSpan
        ) {

            Int64 totalTimeInMilliseconds;
            totalTimeInMilliseconds = timeSpan.Milliseconds;

            if (timeSpan.Minutes > 0) {
                totalTimeInMilliseconds += timeSpan.Minutes * 60 * 1000;
            }

            if (timeSpan.Seconds > 0) {
                totalTimeInMilliseconds += timeSpan.Seconds * 1000;
            }
            return totalTimeInMilliseconds;
        }

        #region Tests for specific test case

        private static void runReqeusts(
            string agentUID,
            string ipAddress,
            string username,
            PerformanceCounter counter,
            StreamWriter sw,
            int iterationsCount,
            int delayTime
        ) {

            Stopwatch stopWatch = new Stopwatch();

            // Write the header line in the data collector file
            sw.WriteLine(DATA_COLLECTOR_FILE_HEADER_LINE);

            // Increment the number of threads initialized.
            Interlocked.Increment(ref _threadsAreInitialized);

            // We want all the threads to start working together
            // so all threads will wait for the following variable
            // to be greater than zero.
            while (_threadsAreInitialized < _threadsCount) {
                Thread.Sleep(100);
            }

            Int64 totalTimeInMilliseconds;

            for (int i = 0; i < iterationsCount; i++) {

                string eventXML = 
                    createEventXMLForWPCDataFetch(
                        new WBXTimeStamp().ToString(),
                        ipAddress,
                        username
                    );

                // Reset the stop watch
                stopWatch.Reset();
                stopWatch.Start();

                // Request for WPC data
                string result =
                    _serviceClient.fetchPolicyData(
                        agentUID,
                        eventXML
                    );

                //Stop the stop watch
                stopWatch.Stop();

                totalTimeInMilliseconds =
                    calculateTimeInMilliseconds(
                        stopWatch.Elapsed
                    );

                // Update performance counter
                //counter.RawValue = stopWatch.Elapsed.Milliseconds;

                // Check for an error
                if (result.IndexOf("error", 0, StringComparison.CurrentCultureIgnoreCase) > -1) {
                    sw.WriteLine(
                        DATA_COLLECTOR_FILE_ERROR_LINE,
                        DateTime.Now.ToString(Constants.TIMESTAMP_FORMAT),
                        eventXML
                    );
                }
                else {
                    // Write the data to the data collector file
                    sw.WriteLine(
                        DATA_COLLECTOR_FILE_DATA_LINE,
                        DateTime.Now.ToString(Constants.TIMESTAMP_FORMAT),
                        totalTimeInMilliseconds
                    );
                }

		        // Flush
                sw.Flush();
                
                // Wait the delay time
                Thread.Sleep(delayTime);
            }

            // Increment the number of threads finished.
            Interlocked.Increment(ref _numberOfThreadsFinished);
        }

        #endregion

    }
}
