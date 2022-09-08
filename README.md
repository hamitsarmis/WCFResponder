# WCFResponder

Are you looking for responding your WCF calls with a dotnet core backend?
If you also have deflate or gzip compression you will have trouble.

Good news is that you can use this project :)

Response is hard-coded in this example but you can get anything from requestMessage variable in the example and then return any response with datacontractserializer.

P.S: You shouldn't use OperationContext.Current in your code.
