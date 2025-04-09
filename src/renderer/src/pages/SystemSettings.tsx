import { FC } from 'react'
import { Form, Input, Checkbox, Button } from 'antd'
import { useSettingStore } from '@renderer/store/settingStore'

export const SystemSettings: FC = () => {
  const [form] = Form.useForm()
  const { settings, setSettingForm } = useSettingStore()
  const save = () => {
    const value = form.getFieldsValue()
    setSettingForm({
      proxy: { uri: value.proxyAddress, token: value.proxyAuth },
      windowsSleep: { disabled: !!value.disableWindowsSleep }
    })
  }
  return (
    <Form
      form={form}
      initialValues={{
        proxyAddress: settings.proxy?.uri || '',
        proxyAuth: settings.proxy?.token
      }}
      style={{ flex: 1, paddingTop: '20px' }}
      labelCol={{ span: 6 }}
      wrapperCol={{ span: 14 }}
    >
      {/* 第一部分：代理服务器设置 */}
      <Form.Item label="代理服务器地址" name="proxyAddress">
        <Input />
      </Form.Item>
      <Form.Item label="代理服务器认证" name="proxyAuth">
        <Input />
      </Form.Item>

      {/* 第二部分：阻止 windows 休眠 */}
      <Form.Item label="阻止 Windows 休眠" name="disableWindowsSleep">
        <Checkbox>开启</Checkbox>
      </Form.Item>

      {/* 第三部分：保存按钮 */}
      <Form.Item label={null}>
        <Button type="primary" onClick={save}>
          保存
        </Button>
      </Form.Item>
    </Form>
  )
}
