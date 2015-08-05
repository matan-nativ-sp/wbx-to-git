using System;
using System.IO;
using System.Diagnostics;

using WBX.whiteOPS.ServerCore;
using WBX.whiteOPS.Tools.PerformanceMonitorHelper;
using WBX.whiteOPS.ServerCore.DataTypes;
using WBXHealthGenerator.AgentConfigurationService;
using System.Collections.Generic;
using System.Threading;
using System.Collections;

namespace WBXHealthGenerator {
    class Program {

        private static Hashtable _wpcUIDs = 
            new Hashtable() { 
                {"ADWPC", "3"},
                {"ePOWPC" , "5"},
                {"CPWPC" , "4"} 
            };
        private static Hashtable _bamUIDs = 
            new Hashtable() { 
                {"WSSBAM" , "1"},
                {"SAPR3BAM" , "2"} 
            };
        private static int _current_agents_configuration_version = 1;

        private const string DATA_COLLECTOR_FILE_HEADER_LINE =
            "timestamp (" + Constants.TIMESTAMP_FORMAT + "),agent uid,milliseconds to process request";
        private const string DATA_COLLECTOR_FILE_DATA_LINE = "{0},{1},{2}";
        private const string DATA_COLLECTOR_FILE_ERROR_LINE = "{0},{1},ERROR: {2}";

        private static string _baseHealthXML =
            "<root>" +
                "<configuration>" +
                    "<attribute name=\"version\" value=\"-1\" />" +
                    "<attribute name=\"configid\" value=\"1\" />" +
                    "<attribute name=\"timestamp\" value=\"10/06/2009 17:46:11\" />" +
                    "<attribute name=\"host\" value=\"wbx-test-app\" />" +
                    "<attribute name=\"ip\" value=\"192.168.2.111\" />" +
                    "<attribute name=\"uid\" value=\"ADWPC\" />" +
                "</configuration>" +
                "<attribute name=\"type\" value=\"WPC\" />" +
               "</root>";

        private static AgentConfigurationServiceClient _serviceClient;
        private static PerformanceCounter[] _counters = null;
        private static StreamWriter[] _streamWriters = null;

        private static int _threadsCount;
        private static int _threadsAreInitialized = 0;
        private static Int64 _numberOfThreadsFinished = 0;

        private delegate void _runTestCase(
            PerformanceCounter counter,
            StreamWriter sw,
            int iterationsCount,
            bool updateConfiguration,
            int delayTime
        );

        static void Main(string[] args) {

            #region Constants

            string PROCESS_ID = Process.GetCurrentProcess().Id.ToString();
            const string COUNTER_NAME = "Health Generator";
            const string COUNTER_HELP = "Health Generator is used to monitor the number of seconds each report took until it got back from the agent configuration manager service.";

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
                new AgentConfigurationServiceClient();

            try {
                _serviceClient.Open();
            }
            catch (Exception ex) {
                Console.Out.Write(
                    "Open Agent Configuration Service failed with exception: " + ex
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
            //Console.ReadKey(true);
            Console.WriteLine(
                "\nProcessing operations of test {0}, please wait...\n",
                testNumber
            );

            bool updateConfiguration = false;
            _runTestCase runTestDelegate = new _runTestCase(runTestsForCase1and2);

            switch (testNumber) {
                case 1:
                    updateConfiguration = false;
                    break;
                case 2:
                    updateConfiguration = true;
                    break;
                default:
                    Console.WriteLine(
                        "Test number {0} does not exist.",
                        testNumber
                    );
                    return;
            }

            if (runTestDelegate != null) {

                // For each counter begin a new thread
                for (int i = 0; i < _threadsCount; i++) {
                    runTestDelegate.BeginInvoke(
                        _counters[i],
                        _streamWriters[i],
                        iterationsCount,
                        updateConfiguration,
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

                System.Threading.Thread.Sleep(2000);
                Console.WriteLine("\nPress any key to terminate performance instance counters...");
                //Console.ReadKey(true);

                foreach (PerformanceCounter counter in _counters) {
                    counter.RemoveInstance();
                }

                foreach (StreamWriter sw in _streamWriters) {
                    sw.Close();
                }
            }
            #endregion
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

        private static StreamWriter openFile(
            string fileName
        ) {

            FileStream fs =
                new FileStream(
                    fileName + ".csv",
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

        private static WBXXml createHealthReport(
            string agentType,
            string agentUID,
            string agentVersion,
            string agentConfigId
        ) {
            WBXXml result = new WBXXml(_baseHealthXML);

            // Update the agent uid in the xml
            result.updateFirstNameValue(
                "uid",
                agentUID
            );

            // Update the agent type in the xml
            result.updateFirstNameValue(
                "type",
                agentType
            );

            // Update the agent version in the xml
            result.updateFirstNameValue(
                "version",
                agentVersion
            );

            // Update the agent config id in the xml
            result.updateFirstNameValue(
                "configid",
                agentConfigId
            );

            return result;
        }

        #region Tests for specific test case

        private static void runTestsForCase1and2(
            PerformanceCounter counter,
            StreamWriter sw,
            int iterationsCount,
            bool updateConfiguration,
            int delayTime
        ) {

            Stopwatch stopWatch = new Stopwatch();
            string currentVersion = _current_agents_configuration_version.ToString();

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

                // If we want to get a configuration update
                // we simply set the current version of the agent
                // configuration to -1
                if (updateConfiguration) {
                    currentVersion = "-1";
                }
    
                #region Create the health reports
                
                // Go over each WPC agent and send his health report.
                foreach (string wpcAgentUID in _wpcUIDs.Keys) {

                    // Create the health string to send
                    WBXXml health =
                        createHealthReport(
                            Constants.AGENT_TYPES.WPC,
                            wpcAgentUID,
                            currentVersion,
                            (string)_wpcUIDs[wpcAgentUID]
                        );

                    // Reset the stop watch
                    stopWatch.Reset();
                    stopWatch.Start();

                    // Send the health report to the agent
                    // configuration manager.
                    string healthResponse = 
                        _serviceClient.reportHealth(
                            health.ToString()
                        );

                    // Stop the stop watch
                    stopWatch.Stop();

                    totalTimeInMilliseconds =
                        calculateTimeInMilliseconds(
                            stopWatch.Elapsed
                        );

                    // Update performance counter
                    //counter.RawValue = stopWatch.Elapsed.Seconds;

                    // Check for an error
                    if (healthResponse.IndexOf("error", 0, StringComparison.CurrentCultureIgnoreCase) > -1) {
                        sw.WriteLine(
                            DATA_COLLECTOR_FILE_ERROR_LINE,
                            DateTime.Now.ToString(Constants.TIMESTAMP_FORMAT),
                            wpcAgentUID,
                            health.ToString()
                        );
                    }
                    else {
                        // Write the data to the data collector file
                        sw.WriteLine(
                            DATA_COLLECTOR_FILE_DATA_LINE,
                            DateTime.Now.ToString(Constants.TIMESTAMP_FORMAT),
                            wpcAgentUID,
                            totalTimeInMilliseconds
                        );
                    }

                    // Wait the delay time
                    System.Threading.Thread.Sleep(delayTime);
                }

                // Go over each BAM agent and send his health report.
                foreach (string bamAgentUID in _bamUIDs.Keys) {

                    // Create the health string to send
                    WBXXml health =
                        createHealthReport(
                            Constants.AGENT_TYPES.BAM,
                            bamAgentUID,
                            currentVersion,
                            (string)_bamUIDs[bamAgentUID]
                        );

                    // Reset the stop watch
                    stopWatch.Reset();
                    stopWatch.Start();

                    // Send the health report to the agent
                    // configuration manager.
                    string healthResponse =
                        _serviceClient.reportHealth(
                            health.ToString()
                        );

                    // Stop the stop watch
                    stopWatch.Stop();

                    totalTimeInMilliseconds =
                        calculateTimeInMilliseconds(
                            stopWatch.Elapsed
                        );

                    // Update performance counter
                    //counter.RawValue = stopWatch.Elapsed.Seconds;

                    // Check for an error
                    if (healthResponse.IndexOf("error", 0, StringComparison.CurrentCultureIgnoreCase) > -1) {
                        sw.WriteLine(
                            DATA_COLLECTOR_FILE_ERROR_LINE,
                            DateTime.Now.ToString(Constants.TIMESTAMP_FORMAT),
                            bamAgentUID,
                            health.ToString()
                        );
                    }
                    else {
                        // Write the data to the data collector file
                        sw.WriteLine(
                            DATA_COLLECTOR_FILE_DATA_LINE,
                            DateTime.Now.ToString(Constants.TIMESTAMP_FORMAT),
                            bamAgentUID,
                            totalTimeInMilliseconds
                        );
                    }

                    // flush
                    sw.Flush();
                
                    // Wait the delay time
                    Thread.Sleep(delayTime);
                }

                #endregion  

            }

            // Increment the number of threads finished.
            Interlocked.Increment(ref _numberOfThreadsFinished);
        }

        #endregion

    }
}
