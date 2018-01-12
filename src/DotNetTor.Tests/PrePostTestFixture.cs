using DotNetEssentials.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace DotNetTor.Tests
{
	public class PrePostTestFixture : IDisposable
	{
		public PrePostTestFixture()
		{
			// ... initialize data in the test database ...
			Logger.SetMinimumLevel(LogLevel.Debug);
			Logger.SetTypes(LogMode.Debug);
			
			// download tor (based on system)
			// run tor
		}

		public void Dispose()
		{
			// ... clean up test data from the database ...
		}
	}
}
