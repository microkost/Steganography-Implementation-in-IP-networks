﻿using PcapDotNet.Packets;
using PcapDotNet.Packets.Ethernet;
using System;
using System.Collections.Generic;

namespace SteganoNetLib
{
    public interface INetNode
    {
        //contains common properties for common roles like Receiver or Sender        

        //steganography parametres
        List<int> StegoUsedMethodIds { get; set; }                

        //network parametres
        string IpSourceInput { get; set; } //not IpV4Address from PcapDotNet (reference izolation)        
        string IpDestinationInput { get; set; } //server not need it         
        ushort PortDestination { get; set; }
        ushort PortSource { get; set; }
        MacAddress MacAddressSource { get; set; }
        MacAddress MacAddressDestination { get; set; }

        //control
        Queue<string> messages { get; set; }
        //bool terminate { get; set; }

        //methods
        bool ArePrerequisitiesDone(); //checking if properties are not null before start
        //void AddInfoMessage(string txt); ////add something to output from everywhere else... Is internal method!

        //string GetSecretMessage(); //no access to Packets because of referencies, izolation of UI
    }
}
