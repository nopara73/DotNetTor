using DotNetTor.Bases;
using DotNetTor.Interfaces;
using DotNetTor.TorOverTcp.Models.Fields;
using DotNetTor.TorOverTcp.Models.Messages;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using Xunit;

namespace DotNetTor.Tests
{
	public class TorOverTcpModelsTests : IClassFixture<SharedFixture>
	{
		private SharedFixture SharedFixture { get; }

		public TorOverTcpModelsTests(SharedFixture fixture)
		{
			SharedFixture = fixture;
		}

		[Fact]
		public void TotVersionTest()
		{
			var x = TotVersion.Version1;
			var x2 = new TotVersion(2);
			var x3 = new TotVersion(1);
			var x4 = new TotVersion();
			x4.FromHex("01");
			var x5 = new TotVersion();
			x5.FromByte(1);

			Assert.NotEqual(x, x2);
			Assert.Equal(x, x3);
			Assert.Equal(x, x4);
			Assert.Equal(x, x5);
			
			new TotVersion(0);
			new TotVersion(255);
			Assert.Throws<ArgumentOutOfRangeException>(() =>
			{
				new TotVersion(-1);
			});
			Assert.Throws<ArgumentOutOfRangeException>(() =>
			{
				new TotVersion(256);
			});

			Assert.Throws<IndexOutOfRangeException>(() =>
			{
				new TotVersion().FromHex("1");
			});
		}

		[Fact]
		public void TotMessageTypeTest()
		{
			var x = TotMessageType.Notification;
			var x2 = TotMessageType.Ping;
			var x3 = new TotMessageType();
			x3.FromHex("05");
			var x4 = new TotMessageType();
			x4.FromByte(5);

			Assert.NotEqual(x, x2);
			Assert.Equal(x, x3);
			Assert.Equal(x, x4);

			Assert.Equal("X'01'", TotMessageType.Request.ToHex(xhhSyntax: true));
			Assert.Equal("02", TotMessageType.Response.ToHex(xhhSyntax: false));
			Assert.Equal("03", TotMessageType.SubscribeRequest.ToHex());
			Assert.Equal("X'05' Notification", TotMessageType.Notification.ToString());
			Assert.Equal("X'06' Ping", TotMessageType.Ping.ToString());
			Assert.Equal("X'07' Pong", TotMessageType.Pong.ToString());
			var x6 = new TotMessageType();
			x6.FromByte(8);
			Assert.Equal("X'08'", x6.ToString());
		}

		[Fact]
		public void TotPurposeTest()
		{
			var x = TotPurpose.Success;
			var x2 = TotPurpose.Empty;
			var x3 = new TotPurpose();
			x3.FromHex("00");
			var x4 = new TotPurpose();
			x4.FromBytes(new byte[] { 0 });

			Assert.NotEqual(x, x2);
			Assert.Equal(x, x3);
			Assert.Equal(x, x4);

			Assert.Equal(TotPurpose.Ping, new TotPurpose("ping"));
			Assert.Equal(TotPurpose.Pong, new TotPurpose("pong"));
			Assert.Equal(new byte[] { }, TotPurpose.Empty.ToBytes());
			Assert.Equal(new TotPurpose(""), TotPurpose.Empty);
			Assert.Equal(new TotPurpose(new byte[] { }), TotPurpose.Empty);
			Assert.NotEqual(new TotPurpose(), TotPurpose.Empty);
			Assert.Equal("", TotPurpose.Empty.ToString());
			Assert.Equal("X'00' Success", TotPurpose.Success.ToString());
			Assert.Equal("X'01' BadRequest", TotPurpose.BadRequest.ToString());
			Assert.Equal("X'02' VersionMismatch", TotPurpose.VersionMismatch.ToString());
			Assert.Equal("X'03' UnsuccessfulRequest", TotPurpose.UnsuccessfulRequest.ToString());
			Assert.Equal("ping", TotPurpose.Ping.ToString());
			Assert.Equal("pong", TotPurpose.Pong.ToString());

			Assert.Equal(3, new TotPurpose("foo").Length);
			Assert.Equal(4, new TotPurpose("bér").ToBytes(startsWithLength: false).Length);
			Assert.Equal(5, new TotPurpose("bór").ToBytes(startsWithLength: true).Length);
			
			var bigStringBuilder = new StringBuilder();
			for(int i = 0; i < 255; i++)
			{
				bigStringBuilder.Append("0");
			}
			new TotPurpose(bigStringBuilder.ToString());
			bigStringBuilder.Append("0");
			Assert.Throws<ArgumentOutOfRangeException>(() => new TotPurpose(bigStringBuilder.ToString()));
		}

		[Fact]
		public void TotContentTest()
		{
			var x = TotContent.CantRequestSubscribeNotifyChannel;
			var x2 = TotContent.Empty;
			var x3 = new TotContent();
			x3.FromHex("0003040506070909");
			var x4 = new TotContent();
			x4.FromBytes(new byte[] { 0, 3, 4, 5, 6, 7, 9, 9 });

			Assert.NotEqual(x, x2);
			Assert.Equal(x3, x4);

			Assert.Equal(new TotContent("Cannot send Request to a SubscribeNotify channel."), TotContent.CantRequestSubscribeNotifyChannel);
			Assert.Equal(new TotContent("Cannot send SubscribeRequest to a RequestResponse channel."), TotContent.CantSubscribeRequestRequestResponseChannel);
			Assert.Equal(new byte[] { }, TotContent.Empty.ToBytes());
			Assert.Equal(new TotContent(""), TotContent.Empty);
			Assert.Equal(new TotContent(new byte[] { }), TotContent.Empty);
			Assert.NotEqual(new TotContent(), TotContent.Empty);
			Assert.Equal("", TotContent.Empty.ToString());
			Assert.Equal("foo", new TotContent("foo").ToString());
			Assert.Equal("féo", new TotContent("féo").ToString());

			Assert.Equal(3, new TotContent("foo").Length);
			Assert.Equal(4, new TotContent("bér").ToBytes(startsWithLength: false).Length);
			Assert.Equal(8, new TotContent("bór").ToBytes(startsWithLength: true).Length);
			Assert.Equal(7, new TotContent("bór", Encoding.ASCII).ToBytes(startsWithLength: true).Length);

			var bigStringBuilder = new StringBuilder();
			for (int i = 0; i < 536870912; i++)
			{
				bigStringBuilder.Append("0");
			}
			new TotContent(bigStringBuilder.ToString());
			bigStringBuilder.Append("0");
			Assert.Throws<ArgumentOutOfRangeException>(() => new TotContent(bigStringBuilder.ToString()));

			var x5 = new TotContent();
			x5.FromBytes(new byte[] { 3, 0, 0, 0, 1, 2, 3 }, startsWithLength: true);
			x5.FromBytes(new byte[] { 0, 0, 0, 0 }, startsWithLength: true);
			Assert.Throws<FormatException>(() => x5.FromBytes(new byte[] { 3, 0, 0, 0, 1, 2 }, startsWithLength: true));
			Assert.Throws<FormatException>(() => x5.FromBytes(new byte[] { 3, 0, 0 }, startsWithLength: true));
		}

		[Fact]
		public void TotMessageTest()
		{
			Assert.Equal(TotPurpose.Ping, TotPing.Instance.Purpose);
			Assert.Equal(TotPurpose.Pong, TotPong.Instance.Purpose);

			Assert.Equal(TotPurpose.Success, TotResponse.Success.Purpose);
			Assert.Equal(TotPurpose.BadRequest, TotResponse.BadRequest.Purpose);
			Assert.Equal(TotPurpose.VersionMismatch, TotResponse.VersionMismatch.Purpose);
			Assert.Equal(TotPurpose.UnsuccessfulRequest, TotResponse.UnsuccessfulRequest.Purpose);

			var x = new TotRequest("status");

			Assert.Equal(97, x.GetLastCellFullnessPercentage());
			Assert.Equal(1, x.GetNumberOfCells());
			Assert.Equal(499, x.GetNumberOfDummyBytesInLastCell());
		}
	}
}
