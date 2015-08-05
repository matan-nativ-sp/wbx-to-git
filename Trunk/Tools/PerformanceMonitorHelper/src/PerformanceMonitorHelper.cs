using System;
using System.Diagnostics;

namespace WBX.whiteOPS.Tools.PerformanceMonitorHelper {
    
    /// <summary>
    /// Performance Monitor helper class.
    /// This code was copied from the TechRepublic blog:
    /// http://blogs.techrepublic.com.com/programming-and-development/?p=645
    /// </summary>
    // Created: 17/6/2009
    // Author: Itay Maichel
    // FileName: PerformanceMonitorHelper.cs
    public static class PerformanceMonitorHelper {

        #region Constants

        public const string WBX_CATEGORY_NAME = "WhiteOPS";
        public const string WBX_CATEGORY_HELP = "WhiteOPS category for the application modules counters";

        #endregion

        /// <summary>
        /// This method will create a new counter category.
        /// 
        /// Note that only one counter is added in this code.
        /// If more than one counter needs to be created simply copy 
        /// the code that creates the sampleCounter object and add it 
        /// to the counterData array. 
        /// Any counters added to the counterData array will be created 
        /// when you call the Create method on the PerformanceCounterCategory 
        /// object.
        /// 
        /// Also notice the "SingleInstance" portion of the Create() call.
        /// This disables multiple instances for the specified counter.
        /// If you need multiple instances you can simply change "SingleInstance"
        /// to "MultiInstance" when you call the Create method.
        ///
        /// Once the Create() method is called the counter is ready for you to 
        /// write data to it.
        /// </summary>
        /// <param name="categoryName">Category name</param>
        /// <param name="categoryHelp">Category help text</param>
        /// <param name="categoryType">Category type</param>
        /// <param name="counterName">Counter name</param>
        /// <param name="counterHelp">Counter help text</param>
        /// <param name="counterType">Counter type</param>
        public static void createCounter(
            string categoryName,
            string categoryHelp,
            PerformanceCounterCategoryType categoryType,
            string counterName,
            string counterHelp,
            PerformanceCounterType counterType
        ) {

            if (!PerformanceCounterCategory.Exists(categoryName)) {

                // Create the collection that will hold the data
                // for the counters we are creating.
                CounterCreationDataCollection counterData =
                    new CounterCreationDataCollection();

                // Create the CreationData object
                CounterCreationData counter =
                    new CounterCreationData();

                // Set the counter's type, name and help text
                counter.CounterType = counterType;
                counter.CounterName = counterName;
                counter.CounterHelp = counterHelp;

                // Add the creation data object to
                // our collection
                counterData.Add(counter);

                // Create the counter in the system using
                // the collection
                PerformanceCounterCategory.Create(
                    categoryName,
                    categoryHelp,
                    categoryType,
                    counterData
                );
            }
        }

        /// <summary>
        /// This method will delete, if exists, the category
        /// with the given name.
        /// </summary>
        /// <param name="categoryName">Category name</param>
        public static void deleteCategory(
            string categoryName
        ) {

            if (PerformanceCounterCategory.Exists(categoryName)) {
                PerformanceCounterCategory.Delete(categoryName);
            }
        }

        /// <summary>
        /// In this method we instantiate the PerformanceCounter object and return it.
        /// </summary>
        /// <param name="categoryName">Category name</param>
        /// <param name="counterName">Counter name</param>
        /// <param name="readOnly">Is counter for read only?</param>
        /// <returns>Performance counter</returns>
        public static PerformanceCounter getCounter(
            string categoryName,
            string counterName,
            bool readOnly
        ) {

            PerformanceCounter counter = null;

            // Check if the category exists
            if (PerformanceCounterCategory.Exists(categoryName)) {

                counter =
                    new PerformanceCounter(
                        categoryName,
                        counterName,
                        readOnly
                    );
            }

            return counter;
        }

        /// <summary>
        /// In this method we instantiate the PerformanceCounter object and return it.
        /// </summary>
        /// <param name="categoryName">Category name</param>
        /// <param name="counterName">Counter name</param>
        /// <param name="instanceName">Counter instance name</param>
        /// <param name="instanceLifeTime">The life time of this instance</param>
        /// <param name="readOnly">Is counter for read only?</param>
        /// <returns>Performance counter</returns>
        public static PerformanceCounter getCounter(
            string categoryName,
            string counterName,
            string instanceName,
            PerformanceCounterInstanceLifetime instanceLifeTime,
            bool readOnly
        ) {

            PerformanceCounter counter = null;

            // Check if the category exists
            if (PerformanceCounterCategory.Exists(categoryName)) {
                counter =
                    new PerformanceCounter(
                        categoryName,
                        counterName,
                        instanceName,
                        readOnly
                    );
            }
            
            return counter;
        }

        /// <summary>
        /// This code simply sets the PerformanceCounter.RawValue property to 
        /// the given value.
        /// The RawValue property is the current value of the PerformanceCounter,
        /// and that is the value that will be displayed when you read the 
        /// PerformanceCounter.
        /// </summary>
        /// <param name="counter">Counter</param>
        /// <param name="rawValue">Value</param>
        public static void setCounterRawValue(
            PerformanceCounter counter,
            Int64 rawValue
        ) {
            counter.RawValue = rawValue;
        }
    }

}
