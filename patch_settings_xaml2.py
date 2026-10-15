import re

file_path = "SidebarDiagnostics/Settings.xaml"
with open(file_path, "r", encoding="utf-8") as f:
    content = f.read()

find_str = '''                                                      <StackPanel Grid.Column="5" Orientation="Horizontal" VerticalAlignment="Center">
                                                          <Button Content="&#x25B2;" Width="20" Height="20" Margin="0,0,2,0" Click="HardwareUp_Click" ToolTip="Move Up" />
                                                          <Button Content="&#x25BC;" Width="20" Height="20" Click="HardwareDown_Click" ToolTip="Move Down" />
                                                      </StackPanel>
                                                  </Grid>
                                              </DataTemplate>
                                          </ItemsControl.ItemTemplate>'''

replace_str = '''                                                      <StackPanel Grid.Column="5" Orientation="Horizontal" VerticalAlignment="Center">
                                                          <Button Content="&#x25B2;" Width="20" Height="20" Margin="0,0,2,0" Click="HardwareUp_Click" ToolTip="Move Up" />
                                                          <Button Content="&#x25BC;" Width="20" Height="20" Click="HardwareDown_Click" ToolTip="Move Down" />
                                                      </StackPanel>
                                                  </Grid>
                                                  
                                                  <StackPanel x:Name="ClaudeDetails" Visibility="Collapsed" Orientation="Horizontal" Margin="30,0,0,5">
                                                      <Button Content="{x:Static frame:Resources.SettingsUsageClaudeRenewButton}" Click="HardwareClaudeRenew_Click" ToolTip="{x:Static frame:Resources.SettingsUsageClaudeRenewTooltip}" Width="60" Margin="0,0,10,0" Padding="5,2" />
                                                      <CheckBox Content="{x:Static frame:Resources.SettingsUsageClaudeAutoRenew}" IsChecked="{Binding Path=AutoRenew, Mode=TwoWay}" VerticalAlignment="Center" Margin="0,0,10,0" />
                                                      <TextBlock Text="{Binding Path=RenewStatus}" VerticalAlignment="Center" FontStyle="Italic" Opacity="0.8" />
                                                  </StackPanel>
                                              </StackPanel>
                                              <DataTemplate.Triggers>
                                                  <DataTrigger Binding="{Binding ID}" Value="AIUsage_Claude">
                                                      <Setter TargetName="ClaudeDetails" Property="Visibility" Value="Visible" />
                                                  </DataTrigger>
                                              </DataTemplate.Triggers>
                                              </DataTemplate>
                                          </ItemsControl.ItemTemplate>'''

# Need to fix the outer container to wrap Grid and StackPanel in a parent Panel
# The DataTemplate needs a single root element (StackPanel).
content = content.replace('''                                              <DataTemplate>
                                                  <Grid Margin="0,5">''', '''                                              <DataTemplate>
                                                <StackPanel>
                                                  <Grid Margin="0,5">''')
content = content.replace(find_str, replace_str)

with open(file_path, "w", encoding="utf-8") as f:
    f.write(content)
