﻿//------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated by a tool.
//     Runtime Version:4.0.30319.18034
//
//     Changes to this file may cause incorrect behavior and will be lost if
//     the code is regenerated.
// </auto-generated>
//------------------------------------------------------------------------------

namespace WBXWPCDataRequestGenerator.WPCFrameworkService {
    
    
    [System.CodeDom.Compiler.GeneratedCodeAttribute("System.ServiceModel", "4.0.0.0")]
    [System.ServiceModel.ServiceContractAttribute(ConfigurationName="WPCFrameworkService.IWPCFrameworkService")]
    public interface IWPCFrameworkService {
        
        [System.ServiceModel.OperationContractAttribute(Action="http://tempuri.org/IWPCFrameworkService/fetchPolicyData", ReplyAction="http://tempuri.org/IWPCFrameworkService/fetchPolicyDataResponse")]
        string fetchPolicyData(string wpcUid, string eventXml);
    }
    
    [System.CodeDom.Compiler.GeneratedCodeAttribute("System.ServiceModel", "4.0.0.0")]
    public interface IWPCFrameworkServiceChannel : WBXWPCDataRequestGenerator.WPCFrameworkService.IWPCFrameworkService, System.ServiceModel.IClientChannel {
    }
    
    [System.Diagnostics.DebuggerStepThroughAttribute()]
    [System.CodeDom.Compiler.GeneratedCodeAttribute("System.ServiceModel", "4.0.0.0")]
    public partial class WPCFrameworkServiceClient : System.ServiceModel.ClientBase<WBXWPCDataRequestGenerator.WPCFrameworkService.IWPCFrameworkService>, WBXWPCDataRequestGenerator.WPCFrameworkService.IWPCFrameworkService {
        
        public WPCFrameworkServiceClient() {
        }
        
        public WPCFrameworkServiceClient(string endpointConfigurationName) : 
                base(endpointConfigurationName) {
        }
        
        public WPCFrameworkServiceClient(string endpointConfigurationName, string remoteAddress) : 
                base(endpointConfigurationName, remoteAddress) {
        }
        
        public WPCFrameworkServiceClient(string endpointConfigurationName, System.ServiceModel.EndpointAddress remoteAddress) : 
                base(endpointConfigurationName, remoteAddress) {
        }
        
        public WPCFrameworkServiceClient(System.ServiceModel.Channels.Binding binding, System.ServiceModel.EndpointAddress remoteAddress) : 
                base(binding, remoteAddress) {
        }
        
        public string fetchPolicyData(string wpcUid, string eventXml) {
            return base.Channel.fetchPolicyData(wpcUid, eventXml);
        }
    }
}