﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using PcapDotNet.Core;
using PcapDotNet.Packets;
using PcapDotNet.Packets.Dns;
using PcapDotNet.Packets.Ethernet;
using PcapDotNet.Packets.Http;
using PcapDotNet.Packets.Icmp;
using PcapDotNet.Packets.IpV4;
using PcapDotNet.Packets.Transport;

namespace SteganoNetLib
{
    public class NetSenderClient : INetNode
    {
        //steganography public parametres
        public volatile bool Terminate = false; //finishing endless speaking
        public List<int> StegoUsedMethodIds { get; set; } //list of IDs of steganographic techniques
        public string SecretMessage { get; set; } //encrypted message to transfer
        public Queue<string> Messages { get; set; } //status and debug info        


        //timers public - saved to DelayInMs when used                
        public const int delayGeneral = 400; //gap between all packets in miliseconds
        public const int delayIcmp = 1000; //gap for ICMP requests (default 1000)
        public const int IpIdentificationChangeSpeedInMs = 10000; //timeout break for ip identification field - RFC value is 120000 ms = 2 mins
        public const int delayDns = 3000;
        public const int delayHttp = 4000;


        //network public parametres
        public ushort PortRemoteDns = 53; //where to expect "fake" DNS service on server side > != PortRemote when DNS methods
        public ushort PortRemote { get; set; }
        public ushort PortLocal { get; set; }
        public MacAddress MacAddressLocal { get; set; }
        public MacAddress MacAddressRemote { get; set; }


        //internal properties based on re-typed public ctor
        private PacketDevice selectedDevice = null;
        private IpV4Address IpOfInterface { get; set; } //isolation of referencies (builded in ctor from string)
        private IpV4Address IpOfRemoteHost { get; set; } //isolation of referencies
        private List<StringBuilder> StegoBinary { get; set; } //contains parts of long steganography string in binary
        private int DelayInMs { get; set; } //how long to wait between iterations of sending


        //network private parametres = methods value keepers 
        static Random rnd = new Random();
        private Stopwatch Timer { get; set; } //IP identification timer
        private bool FirstRun { get; set; } //IP identification decision bit
        private ushort IpIdentification { get; set; } //IP identification value field
        internal uint SeqNumberLocal { get; set; } //for TCP requests - outgoing value (what is in packet)
        internal uint? SeqNumberRemote { get; set; } //for TCP answers - incoming (what expect from other side)
        internal uint AckNumberLocal { get; set; } //for TCP requests - outgoing value
        internal uint AckNumberRemote { get; set; } //for TCP answers - incoming value
        private bool IsTerminatingTCP { get; set; } //TCP flow control
        private bool IsEnstablishedTCP { get; set; } //TCP flow control

        private List<String> DomainsToAsk { get; set; } //dns domains


        public NetSenderClient(string ipOfSendingInterface, ushort portSendFrom, string ipOfReceivingInterface, ushort portSendTo)
        {
            //network ctor
            try
            {
                this.IpOfInterface = new IpV4Address(ipOfSendingInterface);
            }
            catch
            {
                this.IpOfInterface = new IpV4Address("0.0.0.0"); //when comes non-sence from params etc.
                AddInfoMessage("Arrived value of IP (" + ipOfSendingInterface + ") was wrong. Changed to " + IpOfInterface.ToString());
            }

            try
            {
                this.IpOfRemoteHost = new IpV4Address(ipOfReceivingInterface);
            }
            catch
            {
                this.IpOfRemoteHost = new IpV4Address("0.0.0.0");
                AddInfoMessage("Arrived value of IP (" + ipOfReceivingInterface + ") was wrong. Changed to " + IpOfRemoteHost.ToString());
            }
            this.PortLocal = portSendFrom;
            this.PortRemote = portSendTo;
            this.MacAddressLocal = NetStandard.GetMacAddressFromArp(IpOfInterface);
            this.MacAddressRemote = NetStandard.GetMacAddressFromArp(IpOfRemoteHost);

            //bussiness ctor            
            Messages = new Queue<string>();
            Timer = new Stopwatch();
            this.FirstRun = true;
            DelayInMs = delayGeneral;
            SeqNumberLocal = 0; //TODO change to constant here and inside GetTcpLayer()
            AckNumberLocal = 0; //TODO change to constant here and inside GetTcpLayer()
            IsEnstablishedTCP = false; //TCP flow control
            IsTerminatingTCP = false; //TCP flow control
            DomainsToAsk = null; //DNS+HTTP
            AddInfoMessage("Client created...");
        }

        public void Speaking() //thread main method
        {
            if (!ArePrerequisitiesDone()) //check values in properties
            {
                AddInfoMessage("Client is not ready to start, check initialization values...");
                AddInfoMessage("Press ESC to exit"); //TODO confusing in GUI
                return;
            }

            SecretMessage = DataOperations.StringASCII2BinaryNumber(SecretMessage); //convert messsage to binary
            selectedDevice = NetDevice.GetSelectedDevice(IpOfInterface); //take the selected adapter            

            if (StegoUsedMethodIds.Any(NetSteganography.GetListMethodsId(NetSteganography.DnsRangeStart, NetSteganography.DnsRangeEnd, NetSteganography.GetListStegoMethodsIdAndKey()).Contains)) //if current methods contains DNS method ids.
            {
                //if something with dns is used then prepare list of distinct domain names, running once                
                DomainsToAsk = NetDevice.GetDomainsForDnsRequest(false);
                AddInfoMessage("DNS is going to be used. Prepared " + DomainsToAsk.Count + " to request");
                AddInfoMessage("DNS will ask port " + PortRemoteDns + ", otherwise is remote port " + PortRemote);
            }

            using (PacketCommunicator communicator = selectedDevice.Open(65536, PacketDeviceOpenAttributes.Promiscuous, 1000))
            {
                AddInfoMessage(String.Format("Sending prepared on {0} = {1}...", IpOfInterface, selectedDevice.Description));
                AddInfoMessage(String.Format("Server settings: Local: {0}:{1}, Remote: {2}:{3}", IpOfInterface, PortLocal, IpOfRemoteHost, PortRemote));

                do
                {
                    //creating implicit layers
                    List<Layer> layers = new List<Layer>(); //list of used layers
                    layers.Add(NetStandard.GetEthernetLayer(MacAddressLocal, MacAddressRemote)); //L2                    


                    //IP methods
                    List<int> ipSelectionIds = NetSteganography.GetListMethodsId(NetSteganography.IpRangeStart, NetSteganography.IpRangeEnd, NetSteganography.GetListStegoMethodsIdAndKey()); //selected all existing int ids in range of IP codes
                    if (StegoUsedMethodIds.Any(ipSelectionIds.Contains))
                    {
                        IpV4Layer ipV4Layer = NetStandard.GetIpV4Layer(IpOfInterface, IpOfRemoteHost); //L3                         

                        //handling method IpIdentificationMethod
                        if (Timer.ElapsedMilliseconds > IpIdentificationChangeSpeedInMs)
                        {
                            FirstRun = true;
                            AddInfoMessage("\t>Timer reseted after: " + Timer.ElapsedMilliseconds);
                            Timer.Restart();
                        }
                        if (FirstRun == false && StegoUsedMethodIds.Contains(NetSteganography.IpIdentificationMethod))
                        {
                            ipV4Layer.Identification = IpIdentification; //put there previous identification = do not change until time expire...
                        }

                        //generic for all methods (name "first" run is confusing, used when new IP identification field is inserted)
                        Tuple<IpV4Layer, string> ipStego = NetSteganography.SetContent3Network(ipV4Layer, StegoUsedMethodIds, SecretMessage, this, FirstRun);
                        ipV4Layer = ipStego.Item1; //save layer containing steganography
                        SecretMessage = ipStego.Item2; //save rest of unsended bites

                        //handling method IpIdentificationMethod
                        if (FirstRun && StegoUsedMethodIds.Contains(NetSteganography.IpIdentificationMethod))
                        {
                            Timer.Start(); //for timeout of 303
                            IpIdentification = ipV4Layer.Identification; //save stego info and reuse it for next two mins
                            FirstRun = false;
                        }
                        else
                        {
                            if (IpIdentification != ipV4Layer.Identification && StegoUsedMethodIds.Contains(NetSteganography.IpIdentificationMethod))
                            {
                                IpIdentification = ipV4Layer.Identification; //when timer expire, save new value for next iteration
                            }
                        }

                        layers.Add(ipV4Layer); //mark layer as done     
                        DelayInMs = delayGeneral;
                    }
                    else
                    {
                        //if not stego IP selected, add normal IP layer - they need to be in proper order otherwise "Can't determine protocol automatically from next layer because there is no next layer"
                        IpV4Layer ipV4Layer = NetStandard.GetIpV4Layer(IpOfInterface, IpOfRemoteHost); //L3 
                        layers.Add(ipV4Layer);
                        DelayInMs = delayGeneral;
                    }


                    //ICMP methods
                    List<int> icmpSelectionIds = NetSteganography.GetListMethodsId(NetSteganography.IcmpRangeStart, NetSteganography.IcmpRangeEnd, NetSteganography.GetListStegoMethodsIdAndKey());
                    if (StegoUsedMethodIds.Any(icmpSelectionIds.Contains))
                    {
                        IcmpEchoLayer icmpLayer = new IcmpEchoLayer(); //here is problem in creating correct layer because ICMP doesnt have instanstable generic one
                        Tuple<IcmpEchoLayer, string> icmpStego = NetSteganography.SetContent3Icmp(icmpLayer, StegoUsedMethodIds, SecretMessage, this);
                        icmpLayer = icmpStego.Item1; //save layer containing steganography
                        SecretMessage = icmpStego.Item2; //save rest of unsended bites
                        layers.Add(icmpLayer); //mark layer as done
                        DelayInMs = delayIcmp;
                    }


                    //UDP methods - not implemented


                    //TCP methods
                    List<int> tcpSelectionIds = NetSteganography.GetListMethodsId(NetSteganography.TcpRangeStart, NetSteganography.TcpRangeEnd, NetSteganography.GetListStegoMethodsIdAndKey());
                    if (StegoUsedMethodIds.Any(tcpSelectionIds.Contains))
                    {
                        //part of HTTP methods
                    }


                    //DNS methods
                    List<int> dnsSelectionIds = NetSteganography.GetListMethodsId(NetSteganography.DnsRangeStart, NetSteganography.DnsRangeEnd, NetSteganography.GetListStegoMethodsIdAndKey());
                    if (StegoUsedMethodIds.Any(dnsSelectionIds.Contains))
                    {
                        UdpLayer udpLayer = NetStandard.GetUdpLayer(PortLocal, PortRemoteDns); //using 53 hardcoded!
                        layers.Add(udpLayer); //udp is carrying layer

                        //create standard DNS request layer
                        DnsLayer dnsLayer = new DnsLayer();
                        if (DomainsToAsk.Count == 0) { DomainsToAsk = NetDevice.GetDomainsForDnsRequest(false); }
                        string oneDomain = DomainsToAsk[rnd.Next(DomainsToAsk.Count)]; //get random item from list, not unique
                        dnsLayer.IsQuery = true;
                        dnsLayer.Queries = new List<DnsQueryResourceRecord>() { NetStandard.GetDnsQuery(oneDomain) }; //TODO randomly change DnsType

                        //insert steganography changing pre-build layer
                        Tuple<DnsLayer, string> dnsStego = NetSteganography.SetContent7Dns(dnsLayer, StegoUsedMethodIds, SecretMessage, this);
                        dnsLayer = dnsStego.Item1; //save layer containing steganography
                        SecretMessage = dnsStego.Item2; //save rest of unsended bites
                        layers.Add(dnsLayer);
                        DelayInMs = delayDns; //TODO should wait for answer...

                    }


                    //HTTP methods
                    List<int> httpSelectionIds = NetSteganography.GetListMethodsId(NetSteganography.HttpRangeStart, NetSteganography.HttpRangeEnd, NetSteganography.GetListStegoMethodsIdAndKey());
                    if (StegoUsedMethodIds.Any(httpSelectionIds.Contains))
                    {
                        TcpLayer tcpLayer = NetStandard.GetTcpLayer(PortLocal, PortRemote, SeqNumberLocal, AckNumberLocal, TcpControlBits.None); //default for rewrite

                        if (!IsEnstablishedTCP) //let it to make TCP
                        {
                            SeqNumberLocal = (uint)1000; //STEGO IN
                            AckNumberLocal = 0; //STEGO IN
                            SeqNumberRemote = 0; //we dont know
                            AckNumberRemote = SeqNumberLocal + 1; //we dont know + 1

                            tcpLayer = NetStandard.GetTcpLayer(PortLocal, PortRemote, SeqNumberLocal, AckNumberLocal, TcpControlBits.Synchronize);
                            layers.Add(tcpLayer);
                            IsEnstablishedTCP = MakeTcpHandshake(layers, this, true); //is updating global properties Seq/AckNumber
                            continue; //since other stego layers are gone
                        }
                        else //TCP enstablished
                        {
                            //TODO SOLVE TERMINATION...

                            tcpLayer.ControlBits = (TcpControlBits.Push | TcpControlBits.Acknowledgment);

                            HttpRequestLayer httpLayer = new HttpRequestLayer
                            {
                                Version = HttpVersion.Version11,
                                Header = new HttpHeader(new HttpContentLengthField(11)),
                                Body = new Datagram(Encoding.ASCII.GetBytes("hello world")),
                                Method = new HttpRequestMethod(HttpRequestKnownMethod.Get),
                                Uri = @"http://pcapdot.net/",
                            };
                            //steganography to http
                            try //TMP REMOVING some DATA
                            {
                                SecretMessage = SecretMessage.Remove(0, 16);
                            }
                            catch { }

                            layers.Add(tcpLayer);
                            layers.Add(httpLayer);
                            uint tcpPayloadSize = (uint)SendPacket(layers); //sending packet now, not at the end of method due to waiting for ack...                             
                            SeqNumberLocal += tcpPayloadSize; //The sequence number of the client has been increased because of the last packet it sent.

                            
                            //WAIT for ACK of sended DATA
                            //Having received some bytes of data from the server, the client increases its acknowledgement number from 1 to 1449.
                            SeqNumberRemote = NetStandard.WaitForTcpAck(IpOfInterface, IpOfRemoteHost, PortLocal, PortRemote, SeqNumberLocal);
                            if (SeqNumberRemote == null)
                            {
                                AddInfoMessage("Problem with receiving...");
                            }
                            else
                            {
                                AddInfoMessage("ACK updated");
                                AckNumberLocal = (uint)SeqNumberRemote;
                            }    
                            

                            if (SecretMessage.Length == 0)
                            {
                                AddInfoMessage("TCP is Terminating");
                                //send termination packet
                                //MakeTcpHandshakeEnd()
                                Terminate = true;
                            }

                            AddInfoMessage("HTTP+TCP DATA sent of size " + tcpPayloadSize);
                            AddInfoMessage(String.Format("{0} bits of TCP left to send", SecretMessage.Length));

                            DelayInMs = delayHttp;
                            System.Threading.Thread.Sleep(DelayInMs*2);
                            continue;
                        }
                    }

                    //more methods

                    //protection methods, if not enought layers from selection
                    if (layers.Count < 3) //TODO RETURN TO 3
                    {
                        //TODO layers need to be correctly ordered! Cannot append L2 to end...

                        if (!layers.OfType<EthernetLayer>().Any()) //if not contains Etherhetnet layer object
                        {
                            AddInfoMessage("Added L2 in last step");
                            layers.Add(NetStandard.GetEthernetLayer(MacAddressLocal, MacAddressRemote)); //L2
                            DelayInMs = delayGeneral;
                        }

                        if (!layers.OfType<IpV4Layer>().Any())
                        {
                            AddInfoMessage("Added L3 IP in last step");
                            IpV4Layer ipV4LayerTMP = NetStandard.GetIpV4Layer(IpOfInterface, IpOfRemoteHost); //L3
                            layers.Add(ipV4LayerTMP);
                            DelayInMs = delayGeneral;
                        }

                        if (!layers.OfType<IcmpEchoLayer>().Any())
                        {
                            AddInfoMessage("Added L3 ICMP in last step");
                            Tuple<IcmpEchoLayer, string> icmpStegoTMP = NetSteganography.SetContent3Icmp(new IcmpEchoLayer(), new List<int> { NetSteganography.IcmpGenericPing }, SecretMessage, this);
                            layers.Add(icmpStegoTMP.Item1);
                            DelayInMs = delayIcmp;
                        }

                        //TODO TCP protection?
                    }

                    AddInfoMessage(String.Format("{0} bits left to send, waiting {1} ms for next", SecretMessage.Length, DelayInMs));
                    if (SecretMessage.Length == 0)
                    {
                        AddInfoMessage(String.Format("All message departured, you can stop the process by pressing ESC")); //TODO it's confusing when is running from GUI
                        Terminate = true;
                    }

                    //build packet and send
                    PacketBuilder builder = new PacketBuilder(layers);
                    Packet packet = builder.Build(DateTime.Now); //if exception "Can't determine ether type automatically from next layer", you need to put layers to proper order as RM ISO/OSI specifies...
                    communicator.SendPacket(packet);
                    System.Threading.Thread.Sleep(DelayInMs);
                }
                while (!Terminate || SecretMessage.Length != 0);


            }


        }

        public bool ArePrerequisitiesDone()
        {
            //do actual method list contains keys from "database"?
            if (StegoUsedMethodIds.Intersect(NetSteganography.GetListStegoMethodsIdAndKey().Keys).Any() == false)
            {
                AddInfoMessage("Error! Provided keys are not in list of valid keys.");
                return false;
            }

            if (SecretMessage == null || SecretMessage.Length == 0) //when there is no secret to transffer (wrong initialization)
            {
                AddInfoMessage("Error! Secret is not available, wrong initialization.");
                return false;
            }

            //should test SecretMessage for containing other characters than 0 / 1

            if (MacAddressLocal.Equals("{00:00:00:00:00:00}") || MacAddressRemote.Equals("{00:00:00:00:00:00}"))
            {
                AddInfoMessage("Warning! Mac addresses contains suscpicious values.");
            }

            if (IpOfRemoteHost == null || IpOfInterface == null)
            {
                AddInfoMessage("Error! IP addresses are wrongly initialized.");
                return false;
            }

            try
            {
                selectedDevice = NetDevice.GetSelectedDevice(IpOfInterface); //take the selected adapter
                using (PacketCommunicator communicatorTMP = selectedDevice.Open(65536, PacketDeviceOpenAttributes.Promiscuous, 1000))
                {
                    //try to use provide interface, if IP is not valid then it makes error... 
                }
            }
            catch
            {

                AddInfoMessage("Error! Inserted interface cannot be opened. (check value of " + IpOfInterface + " and run again)");
                return false;
            }

            if (PortRemote == 0 || PortLocal == 0)
            {
                AddInfoMessage("Warning! Ports are set to 0, network issue expected.");
            }

            return true;
        }

        public void AddInfoMessage(string txt) //add something to output from everywhere else...
        {
            this.Messages.Enqueue(txt);
            return;
        }

        public bool AskTermination()
        {
            return this.Terminate;
        }

        private static bool MakeTcpHandshake(List<Layer> layers, NetSenderClient ns, bool separateSynAck)
        {
            ns.SendPacket(layers);

            ns.AckNumberLocal = ns.SeqNumberLocal + 1; //expected value from oposite side
            ns.AckNumberRemote = ns.SeqNumberLocal + 1; //because we know it

            uint? seqNumHere = NetStandard.WaitForTcpAck(ns.IpOfInterface, ns.IpOfRemoteHost, ns.PortLocal, ns.PortRemote, ns.AckNumberRemote, TcpControlBits.Synchronize | TcpControlBits.Acknowledgment); //in ack is expected value
            if (seqNumHere == null)
            {
                ns.AddInfoMessage("TCP SYN ACK not received!");
                ns.AckNumberRemote -= 1; //remove iteraction to resend
                return false;
            }
            else
            {
                ns.SeqNumberRemote = seqNumHere;
                ns.AddInfoMessage("TCP SYN ACK received.");
            }

            //update values which are going to be used next datagram
            ns.SeqNumberLocal = ns.AckNumberRemote;
            ns.AckNumberLocal = (uint)ns.SeqNumberRemote + 1;

            if (separateSynAck) //SEND SYN ACK when not in next layer
            {
                TcpLayer tcpLayer = (TcpLayer)layers.Last(); //save last layer
                layers.Remove(layers.Last()); //remove last layer
                tcpLayer = NetStandard.GetTcpLayer(tcpLayer.SourcePort, tcpLayer.DestinationPort, ns.SeqNumberLocal, ns.AckNumberLocal, TcpControlBits.Acknowledgment);
                layers.Add(tcpLayer); //readd last layer
                int packetSize = ns.SendPacket(layers);

                if (packetSize != (ns.AckNumberLocal - ns.SeqNumberRemote - 1))
                {
                    ns.AddInfoMessage("ACK WILL FAIL");
                }
            }

            return true;
        }

        public int SendPacket(List<Layer> layers) //send just from list of layers, building and forwarning the answer
        {
            if (layers == null) { return 0; } //extra protection

            if (layers.Count < 3) //TODO should use complex test of content as client method
            {
                AddInfoMessage("Warning: Count of layers in reply packet is low!");
            }

            try
            {
                selectedDevice = NetDevice.GetSelectedDevice(IpOfInterface); //take the selected adapter
                using (PacketCommunicator communicator = selectedDevice.Open(65536, PacketDeviceOpenAttributes.Promiscuous, 1000))
                {
                    PacketBuilder builder = new PacketBuilder(layers);
                    Packet packet = builder.Build(DateTime.Now);
                    communicator.SendPacket(packet);
                    return packet.Ethernet.IpV4.Tcp.PayloadLength;
                }
            }
            catch
            {
                AddInfoMessage("Error: Packet was NOT send due to error!");
                return 0; //extra protection
            }
        }
    }
}
