using System;
using System.IO;
using System.Threading;
using System.Diagnostics;
using System.Collections;

using WBX.whiteOPS.ServerCore;
using WBX.whiteOPS.Tools.PerformanceMonitorHelper;
using WBX.whiteOPS.ServerCore.DataTypes;
using WBXEventGenerator.EventCollectorService;
using System.Collections.Generic;

namespace WBXEventGenerator {

    class Program {

        private const string DATA_COLLECTOR_FILE_HEADER_LINE =
            "timestamp (" + Constants.TIMESTAMP_FORMAT + "),milliseconds to process request";
        private const string DATA_COLLECTOR_FILE_DATA_LINE = "{0},{1}";
        private const string DATA_COLLECTOR_FILE_ERROR_LINE = "{0},ERROR: {1}";

        // Create base event for updating
        private const string BASE_EVENT =
            "<root>" +
                "<baminfo>" +
                    "<attribute name=\"BAMType\" value=\"\" />" +
                    "<attribute name=\"BAMUniqueID\" value=\"\" />" +
                "</baminfo>" +
            "</root>";

        private static EventCollectorServiceClient _serviceClient;
        private static IDictionary<string,PerformanceCounter> _counters = null;
        private static IDictionary<string,StreamWriter> _streamWriters = null;

        private static int _threadsCount;
        private static int _threadsAreInitialized = 0;
        private static Int64 _numberOfThreadsFinished = 0;

        private delegate void _sendEventsFromFile(
            string eventsFilename,
            PerformanceCounter counter,
            StreamWriter sw,
            int eventsInSend,
            int delayTime
        );

        static void Main(string[] args) {

            #region Constants

            string PROCESS_ID = Process.GetCurrentProcess().Id.ToString();
            const string COUNTER_NAME = "Event Generator";
            const string COUNTER_HELP = "Event Generator is used to monitor the number of processed events by the event generator.";

            #endregion

            #region Validate arguments count

            // Check number of arguments
            if (args.Length != 3) {
                Console.WriteLine("Usage: WBXEventGenerator.exe [filename] [events in send] [delay]\n");
                Console.WriteLine("\tfilename: text file with list of files that contain events");
                Console.WriteLine("\tevents in send: number of events to send in a single send operation");
                Console.WriteLine("\tdelay: number of milliseconds to wait between each event");
                return;
            }

            #endregion

            #region Initialize local variables

            String fileName = args[0];
            int eventsInSend;
            int delayTime;
            
            #endregion

            #region Validations

            // Check file existence
            if (!File.Exists(fileName)) {
                Console.WriteLine("The file " + fileName + " does not exists\n");
                return;
            }

            // Check the number of events to send
            if (!int.TryParse(args[1], out eventsInSend)) {
                Console.WriteLine("Invalid events in send number " + args[1]);
                return;
            }
            else {
                if (eventsInSend < 1) {
                    Console.WriteLine("Invalid events in send number " + args[1] +", number must be at least 1");
                    return;
                }
            }

            // Check delay time
            if (!int.TryParse(args[2], out delayTime)) {
                Console.WriteLine("Invalid delay number " + args[1]);
                return;
            }

            #endregion

            #region Create and open the event collector service

            _serviceClient = new EventCollectorServiceClient();

            try {
                _serviceClient.Open();
            }
            catch (Exception ex){
                Console.Out.Write(
                    "Open Event Collector Service failed with exception: " + ex
                );
                return;
            }
            
            #endregion

            #region Read the event files

            IList<string> eventFiles = new List<string>();

            #region Open the event files file

            FileStream fs =
                new FileStream(
                    fileName,
                    FileMode.Open,
                    FileAccess.Read
                );

            StreamReader sr = new StreamReader(fs);

            #endregion

            #region Read the event file names

            // Start reading events
            String currLine = sr.ReadLine();

            // Read until all events are read
            while (currLine != null) {
                try {

                    eventFiles.Add(currLine);

                    // Read the next event
                    currLine = sr.ReadLine();
                }
                #region Exception handling
                catch (Exception ex) {
                    Console.WriteLine(
                        "Error processing the file which contains the event file names.\nError: {0}",
                        ex.Message
                    );
                }
                #endregion
            }

            #endregion

            _threadsCount = eventFiles.Count;

            #endregion

            #region Create performance counters

            // Delete the WhiteOPS category
            PerformanceMonitorHelper.deleteCategory(
                PerformanceMonitorHelper.WBX_CATEGORY_NAME
            );
            
            //string counterInstanceName = COUNTER_NAME + " " + PROCESS_ID;
            _counters = new Dictionary<string,PerformanceCounter>();

            foreach (string eventFilename in eventFiles) {

                _counters.Add(
                    eventFilename,
                    createCounter(
                        COUNTER_NAME,
                        COUNTER_HELP,
                        eventFilename
                    )
                );
            }

            #endregion

            #region Create a data collector stream writer

            _streamWriters = new Dictionary<string, StreamWriter>();

            foreach (string eventFilename in eventFiles) {

                _streamWriters.Add(
                    eventFilename,
                    openFile(
                        eventFilename + "_results_"
                    )
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
            Console.WriteLine("New performance counters were created with the file names read\n");
            Console.WriteLine("Press any key to process events...");
            Console.ReadKey(true);
            Console.WriteLine("\nProcessing events, please wait...\n");

            // Create a delegate to the method that will send
            // the events.
            _sendEventsFromFile sendEventsFromFileDelegate =
                new _sendEventsFromFile(sendEventsFromFile);

            // Create a new thread for each events file
            foreach (string eventFilename in eventFiles) {

                sendEventsFromFileDelegate.BeginInvoke(
                    eventFilename,
                    _counters[eventFilename],
                    _streamWriters[eventFilename],
                    eventsInSend,
                    delayTime,
                    null,
                    null
                );

            }

            // Wait for all threads to finished.
            while (_numberOfThreadsFinished != _threadsCount) {
                Thread.Sleep(1000);
            }

            #region Remove the counter instance
            if (_counters != null) {
                
                System.Threading.Thread.Sleep(2000);
                Console.WriteLine("\nPress any key to terminate performance instance counter...");
                Console.ReadKey(true);

                foreach (PerformanceCounter counter in _counters.Values) {
                    counter.RemoveInstance();
                }

                foreach (StreamWriter sw in _streamWriters.Values) {
                    sw.Close();
                }
            }
            #endregion

            #region Display finish message

            Console.WriteLine("Process finished.");

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

        private static void sendEventsFromFile(
            string eventsFilename,
            PerformanceCounter counter,
            StreamWriter sw,
            int eventsInSend,
            int delayTime
        ) {

            #region Local variables

            Stopwatch stopWatch = new Stopwatch();
            int eventsInXML = 0;
            WBXXml eventsXMLToSend = null;
            bool updateBAMInformation = true;
            string currBAMType = null;
            int eventsRead = 0;

            #endregion

            // Write the header line in the data collector file
            sw.WriteLine(DATA_COLLECTOR_FILE_HEADER_LINE);

            #region Open the events file

            FileStream fs =
                new FileStream(
                    eventsFilename,
                    FileMode.Open,
                    FileAccess.Read
                );

            StreamReader sr = new StreamReader(fs);

            #endregion

            #region Read and convert header names to hashtable

            // Read the XML names line in the events
            String[] pairNameArray = sr.ReadLine().Split(',');

            Hashtable pairNameHsh = new Hashtable();

            for (int i = 0; i < pairNameArray.Length; i++) {

                pairNameHsh.Add(pairNameArray[i], i);
            }

            #endregion

            // Thread initialization complete
            // Increment the number of threads initialized.
            Interlocked.Increment(ref _threadsAreInitialized);

            // We want all the threads to start working together
            // so all threads will wait for the following variable
            // to be greater than zero.
            while (_threadsAreInitialized < _threadsCount) {
                Thread.Sleep(100);
            }

            Int64 totalTimeInMilliseconds;

            // Start reading events
            String currLine = sr.ReadLine();

            // Read until all events are read
            while (currLine != null) {
                try {


                    #region Create the XML to send

                    if (eventsInXML == 0) {
                        eventsXMLToSend = new WBXXml(BASE_EVENT);
                        updateBAMInformation = true;
                    }

                    #endregion

                    // Increment the event counters.
                    eventsInXML++;
                    eventsRead++;
                    //counter.Increment();

                    string[] currLineArray = currLine.Split(',');

                    #region Add a new event element

                    // Create the event element
                    eventsXMLToSend.addElement(
                        null,
                        "event",
                        new Hashtable() { { "eventId", eventsInXML.ToString() } }
                    );

                    string eventXPath =
                        string.Format(
                            "{0}[@{1}='{2}']",
                            "event",
                            "eventId",
                            eventsInXML
                        );

                    // Create the business data element in the event
                    eventsXMLToSend.addElement(
                        eventXPath,
                        "businessdata"
                    );

                    // Create the policy data element in the event
                    eventsXMLToSend.addElement(
                        eventXPath,
                        "policydata"
                    );

                    #endregion

                    #region Update BAM information if needed

                    if (updateBAMInformation) {

                        currBAMType = currLineArray[(int)pairNameHsh["BAMType"]];

                        eventsXMLToSend.updateFirstNameValue(
                            pairNameArray[(int)pairNameHsh["BAMType"]],
                            currBAMType
                        );

                        eventsXMLToSend.updateFirstNameValue(
                            pairNameArray[(int)pairNameHsh["BAMUniqueID"]],
                            currLineArray[(int)pairNameHsh["BAMUniqueID"]]
                        );

                        updateBAMInformation = false;
                    }

                    #endregion

                    #region Add event to the events XML

                    switch (currBAMType.ToLower()) {
                        case "sap r3":
                            addSAPR3Event(
                                eventsXMLToSend,
                                eventXPath,
                                pairNameHsh,
                                currLineArray
                            );
                            break;
                        case "wss":
                            addWSSEvent(
                                eventsXMLToSend,
                                eventXPath,
                                pairNameHsh,
                                currLineArray
                            );
                            break;
                        default:
                            Console.WriteLine(
                                "Unknown BAM type - {0}",
                                currBAMType
                            );
                            break;
                    }

                    #endregion

                    #region Report the events to the event collector service

                    // Report Events (maybe multi-threaded ?)
                    if (eventsInXML == eventsInSend) {

                        // Reset the number of events in the XML
                        // before reading the next event
                        eventsInXML = 0;

                        stopWatch.Reset();
                        stopWatch.Start();

                        //Console.Out.WriteLine(eventsXMLToSend.ToString());
                        _serviceClient.reportEvents(eventsXMLToSend.ToString());

                        stopWatch.Stop();

                        totalTimeInMilliseconds =
                            calculateTimeInMilliseconds(
                                stopWatch.Elapsed
                            );

                        // Update performance counter
                        //counter.RawValue = stopWatch.Elapsed.Milliseconds;

                        // Write the data to the data collector file
                        sw.WriteLine(
                            DATA_COLLECTOR_FILE_DATA_LINE,
                            DateTime.Now.ToString(Constants.TIMESTAMP_FORMAT),
                            totalTimeInMilliseconds
                        );

                        // Flush the buffer
                        sw.Flush();

                        Thread.Sleep(delayTime);
                    }

                    #endregion

                    // Read the next event
                    currLine = sr.ReadLine();
                }
                #region Exception handling
                catch (Exception ex) {
                    Console.WriteLine(
                        "Error processing event number {0} from file {1}.\nError: {2}",
                        eventsRead,
                        eventsFilename,
                        ex.Message
                    );
                }
                #endregion
            }

            sr.Close();

            // Increment the number of threads finished.
            Interlocked.Increment(ref _numberOfThreadsFinished);
        }

        #region Event methods for specific BAM types

        private static void addSAPR3Event(
            WBXXml eventsXMLToSend,
            string eventXPath,
            Hashtable pairNameHsh,
            string[] eventDetails
        ) {

            string tempStr = null;

            #region Add business data

            string businessDataXPath = eventXPath + "/businessdata";

            #region Transaction ID
            tempStr =
                eventDetails[(int)pairNameHsh["TransactionId"]];
                
            if (!string.IsNullOrEmpty(tempStr)) {
                eventsXMLToSend.addPair(
                    businessDataXPath,
                    "TransactionId",
                    tempStr
                );
            }
            #endregion

            #region Report Name
            tempStr =
                eventDetails[(int)pairNameHsh["ReportName"]];

            if (!string.IsNullOrEmpty(tempStr)) {
                eventsXMLToSend.addPair(
                    businessDataXPath,
                    "ReportName",
                    tempStr
                );
            }
            #endregion

            #region Dynpro Number
            tempStr =
                eventDetails[(int)pairNameHsh["DynproNumber"]];
                    
            if (!string.IsNullOrEmpty(tempStr)) {
                eventsXMLToSend.addPair(
                    businessDataXPath,
                    "DynproNumber",
                    tempStr
                );
            }
            #endregion

            #endregion

            #region Add policy data

            string policyDataXPath = eventXPath + "/policydata";

            #region Server Name
            tempStr =
                eventDetails[(int)pairNameHsh["ServerName"]];

            if (!string.IsNullOrEmpty(tempStr)) {
                eventsXMLToSend.addPair(
                    policyDataXPath,
                    "ServerName",
                    tempStr
                );
            }
            #endregion

            #region Timestamp
            tempStr =
                eventDetails[(int)pairNameHsh["TimeStamp"]];

            if (!string.IsNullOrEmpty(tempStr)) {
                eventsXMLToSend.addPair(
                    policyDataXPath,
                    "TimeStamp",
                    tempStr
                );
            }
            #endregion

            #region Ip Address
            tempStr =
                eventDetails[(int)pairNameHsh["IpAddress"]];

            if (!string.IsNullOrEmpty(tempStr)) {
                eventsXMLToSend.addPair(
                    policyDataXPath,
                    "IpAddress",
                    tempStr
                );
            }
            #endregion

            #region Sap User
            tempStr =
                eventDetails[(int)pairNameHsh["SapUser"]];

            if (!string.IsNullOrEmpty(tempStr)) {
                eventsXMLToSend.addPair(
                    policyDataXPath,
                    "SapUser",
                    tempStr
                );
            }
            #endregion

            #region Terminal Id
            tempStr =
                eventDetails[(int)pairNameHsh["TerminalId"]];

            if (!string.IsNullOrEmpty(tempStr)) {
                eventsXMLToSend.addPair(
                    policyDataXPath,
                    "TerminalId",
                    tempStr
                );
            }
            #endregion

            #region Connection Type
            tempStr =
                eventDetails[(int)pairNameHsh["ConnectionType"]];

            if (!string.IsNullOrEmpty(tempStr)) {
                eventsXMLToSend.addPair(
                    policyDataXPath,
                    "ConnectionType",
                    tempStr
                );
            }
            #endregion

            #region User Name
            tempStr =
                eventDetails[(int)pairNameHsh["UserName"]];

            if (tempStr != null) {
                eventsXMLToSend.addPair(
                    policyDataXPath,
                    "UserName",
                    tempStr
                );
            }
            #endregion

            #endregion

        }

        private static void addWSSEvent(
            WBXXml eventsXMLToSend,
            string eventXPath,
            Hashtable pairNameHsh,
            string[] eventDetails
        ) {

            string tempStr = null;

            #region Add business data

            string businessDataXPath = eventXPath + "/businessdata";

            #region Business Service Origin
            tempStr =
                eventDetails[(int)pairNameHsh["businessServiceOrigin"]];

            if (!string.IsNullOrEmpty(tempStr)) {
                eventsXMLToSend.addPair(
                    businessDataXPath,
                    "businessServiceOrigin",
                    tempStr
                );
            }
            #endregion

            #region Item Location
            tempStr =
                eventDetails[(int)pairNameHsh["itemLocation"]];

            if (!string.IsNullOrEmpty(tempStr)) {
                eventsXMLToSend.addPair(
                    businessDataXPath,
                    "itemLocation",
                    tempStr
                );
            }
            #endregion

            #endregion

            #region Add policy data

            string policyDataXPath = eventXPath + "/policydata";

            #region User Action
            tempStr =
                eventDetails[(int)pairNameHsh["userAction"]];

            if (!string.IsNullOrEmpty(tempStr)) {
                eventsXMLToSend.addPair(
                    policyDataXPath,
                    "userAction",
                    tempStr
                );
            }
            #endregion

            #region User Agent
            tempStr =
                eventDetails[(int)pairNameHsh["userAgent"]];

            if (!string.IsNullOrEmpty(tempStr)) {
                eventsXMLToSend.addPair(
                    policyDataXPath,
                    "userAgent",
                    tempStr
                );
            }
            #endregion
            
            #region Authentication
            tempStr =
                eventDetails[(int)pairNameHsh["authentication"]];

            if (!string.IsNullOrEmpty(tempStr)) {
                eventsXMLToSend.addPair(
                    policyDataXPath,
                    "authentication",
                    tempStr
                );
            }
            #endregion

            #region Custom Action
            tempStr =
                eventDetails[(int)pairNameHsh["customAction"]];

            if (!string.IsNullOrEmpty(tempStr)) {
                eventsXMLToSend.addPair(
                    policyDataXPath,
                    "customAction",
                    tempStr
                );
            }
            #endregion

            #region Item Id
            tempStr =
                eventDetails[(int)pairNameHsh["itemId"]];

            if (!string.IsNullOrEmpty(tempStr)) {
                eventsXMLToSend.addPair(
                    policyDataXPath,
                    "itemId",
                    tempStr
                );
            }
            #endregion

            #region Item Type
            tempStr =
                eventDetails[(int)pairNameHsh["itemType"]];

            if (!string.IsNullOrEmpty(tempStr)) {
                eventsXMLToSend.addPair(
                    policyDataXPath,
                    "itemType",
                    tempStr
                );
            }
            #endregion

            #region Event Data
            tempStr =
                eventDetails[(int)pairNameHsh["eventData"]];

            if (!string.IsNullOrEmpty(tempStr)) {
                eventsXMLToSend.addPair(
                    policyDataXPath,
                    "eventData",
                    tempStr
                );
            }
            #endregion

            #region Event Source
            tempStr =
                eventDetails[(int)pairNameHsh["eventSource"]];

            if (!string.IsNullOrEmpty(tempStr)) {
                eventsXMLToSend.addPair(
                    policyDataXPath,
                    "eventSource",
                    tempStr
                );
            }
            #endregion

            #region Location Type
            tempStr =
                eventDetails[(int)pairNameHsh["locationType"]];

            if (tempStr != null) {
                eventsXMLToSend.addPair(
                    policyDataXPath,
                    "locationType",
                    tempStr
                );
            }
            #endregion

            #region Ip Address
            tempStr =
                eventDetails[(int)pairNameHsh["IpAddress"]];

            if (!string.IsNullOrEmpty(tempStr)) {
                eventsXMLToSend.addPair(
                    policyDataXPath,
                    "IpAddress",
                    tempStr
                );
            }
            #endregion

            #region Machine Name
            tempStr =
                eventDetails[(int)pairNameHsh["machineName"]];

            if (tempStr != null) {
                eventsXMLToSend.addPair(
                    policyDataXPath,
                    "machineName",
                    tempStr
                );
            }
            #endregion

            #region TimeStamp
            tempStr =
                eventDetails[(int)pairNameHsh["TimeStamp"]];

            if (tempStr != null) {
                eventsXMLToSend.addPair(
                    policyDataXPath,
                    "TimeStamp",
                    tempStr
                );
            }
            #endregion

            #region Site Id
            tempStr =
                eventDetails[(int)pairNameHsh["siteId"]];

            if (!string.IsNullOrEmpty(tempStr)) {
                eventsXMLToSend.addPair(
                    policyDataXPath,
                    "siteId",
                    tempStr
                );
            }
            #endregion

            #region Source Name
            tempStr =
                eventDetails[(int)pairNameHsh["sourceName"]];

            if (tempStr != null) {
                eventsXMLToSend.addPair(
                    policyDataXPath,
                    "sourceName",
                    tempStr
                );
            }
            #endregion

            #region User Name
            tempStr =
                eventDetails[(int)pairNameHsh["UserName"]];

            if (!string.IsNullOrEmpty(tempStr)) {
                eventsXMLToSend.addPair(
                    policyDataXPath,
                    "UserName",
                    tempStr
                );
            }
            #endregion

            #region User Domain
            tempStr =
                eventDetails[(int)pairNameHsh["userDomain"]];

            if (tempStr != null) {
                eventsXMLToSend.addPair(
                    policyDataXPath,
                    "userDomain",
                    tempStr
                );
            }
            #endregion

            #region WSS UserId
            tempStr =
                eventDetails[(int)pairNameHsh["wssUserId"]];

            if (!string.IsNullOrEmpty(tempStr)) {
                eventsXMLToSend.addPair(
                    policyDataXPath,
                    "wssUserId",
                    tempStr
                );
            }
            #endregion

            #region User Domain
            tempStr =
                eventDetails[(int)pairNameHsh["wssGroups"]];

            if (tempStr != null) {
                eventsXMLToSend.addMultiattribute(
                    policyDataXPath,
                    "wssGroups",
                    tempStr.Split(Constants.MULTIATTRIBUTE_DELIMITER)
                );
            }
            #endregion

            #endregion

        }

        #endregion

    }
}
